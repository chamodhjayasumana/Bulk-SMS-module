using System.Collections.Concurrent;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Enums;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSms.Application.Services;

public class BulkSmsService : IBulkSmsService
{
    private readonly IMobileNumberValidator _validator;
    private readonly IRecipientFileParser _fileParser;
    private readonly ISmsSegmentCalculator _segmentCalculator;
    private readonly ISmsProvider _smsProvider;
    private readonly SmsProviderOptions _options;
    private readonly SmsGatewayOptions _gatewayOptions;
    private readonly IAndroidSmsGateway _androidGateway;
    private readonly ILogger<BulkSmsService> _logger;

    // Simple in-process guard against accidental double submission of identical payloads
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RecentSendKeys = new();

    public BulkSmsService(
        IMobileNumberValidator validator,
        IRecipientFileParser fileParser,
        ISmsSegmentCalculator segmentCalculator,
        ISmsProvider smsProvider,
        IOptions<SmsProviderOptions> options,
        IOptions<SmsGatewayOptions> gatewayOptions,
        IAndroidSmsGateway androidGateway,
        ILogger<BulkSmsService> logger)
    {
        _validator = validator;
        _fileParser = fileParser;
        _segmentCalculator = segmentCalculator;
        _smsProvider = smsProvider;
        _options = options.Value;
        _gatewayOptions = gatewayOptions.Value;
        _androidGateway = androidGateway;
        _logger = logger;
    }

    public async Task<ValidateNumbersResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string? messagePreview = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Bulk SMS validate started. File={FileName}", Path.GetFileName(fileName));

        var raw = await _fileParser.ParseAsync(fileStream, fileName, cancellationToken);
        var result = ValidateRecipients(raw, messagePreview);

        _logger.LogInformation(
            "Bulk SMS validate completed. Total={Total} Valid={Valid} Invalid={Invalid} Duplicates={Duplicates}",
            result.Total, result.Valid, result.Invalid, result.Duplicates);

        return result;
    }

    public ValidateNumbersResult ValidateRecipients(IEnumerable<string?> recipients, string? messagePreview = null)
    {
        var result = _validator.ValidateMany(recipients);
        result.Estimate = _segmentCalculator.Estimate(messagePreview ?? string.Empty, result.Valid);
        return result;
    }

    public async Task<BulkSendResult> SendBulkAsync(BulkSendRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentException("Request is required.");

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Message is required.");

        if (request.Recipients is null || request.Recipients.Count == 0)
            throw new ArgumentException("At least one recipient is required.");

        var validated = _validator.ValidateMany(request.Recipients);
        if (validated.Valid == 0)
            throw new ArgumentException("No valid recipients after validation.");

        if (validated.Valid > _options.MaxRecipientsPerRequest)
            throw new ArgumentException($"Recipient count exceeds maximum of {_options.MaxRecipientsPerRequest}.");

        if (validated.Valid >= _options.LargeBatchConfirmThreshold && !request.Confirmed)
            throw new InvalidOperationException(
                $"Large batch ({validated.Valid} recipients) requires confirmation. Set confirmed=true after user confirms.");

        if (IsAndroidProvider())
        {
            var gatewayStatus = await _androidGateway.GetStatusAsync(cancellationToken);
            if (!gatewayStatus.Online)
                throw new ArgumentException(gatewayStatus.Error ?? "Android gateway is offline.");
        }

        if (!request.IsTest)
        {
            var dedupeKey = BuildDedupeKey(request.Message, validated.Numbers);
            if (!RecentSendKeys.TryAdd(dedupeKey, DateTimeOffset.UtcNow))
            {
                throw new InvalidOperationException("Duplicate send request detected. Please wait before retrying the same batch.");
            }

            // Expire dedupe keys after 2 minutes
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2));
                    RecentSendKeys.TryRemove(dedupeKey, out _);
                }
                catch { /* ignore */ }
            });
        }

        _logger.LogInformation(
            "Bulk SMS send started. Recipients={Count} MessageLength={Length} Provider={Provider}",
            validated.Valid, request.Message.Length, _options.Provider);

        var numbers = validated.Numbers;
        List<SmsRecipientResult> list;

        if (IsAndroidProvider())
        {
            list = await SendThroughAndroidAsync(request.Message, numbers, cancellationToken);
        }
        else
        {
            var results = new ConcurrentBag<SmsRecipientResult>();
            var batchSize = Math.Max(1, _options.BatchSize);
            var maxConcurrency = Math.Max(1, _options.MaxConcurrency);
            var batchIndex = 0;

            for (var offset = 0; offset < numbers.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batchIndex++;
                var batch = numbers.Skip(offset).Take(batchSize).ToList();
                _logger.LogInformation("Batch {BatchIndex} started. Size={Size}", batchIndex, batch.Count);

                using var semaphore = new SemaphoreSlim(maxConcurrency);
                var tasks = batch.Select(async number =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var item = await SendWithRetryAsync(number, request.Message, cancellationToken);
                        results.Add(item);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                if (_options.RetryDelayMs > 0 && offset + batchSize < numbers.Count)
                    await Task.Delay(_options.RetryDelayMs, cancellationToken);

                _logger.LogInformation("Batch {BatchIndex} completed.", batchIndex);
            }

            list = results.ToList();
        }
        var successful = list.Count(r => r.Status == SmsDeliveryStatus.Sent);
        var failed = list.Count(r => r.Status == SmsDeliveryStatus.Failed);

        _logger.LogInformation(
            "Bulk SMS send completed. Total={Total} Successful={Successful} Failed={Failed}",
            list.Count, successful, failed);

        return new BulkSendResult
        {
            Total = list.Count,
            Successful = successful,
            Failed = failed,
            Results = list.OrderBy(r => r.MobileNumber).ToList()
        };
    }

    private bool IsAndroidProvider() =>
        string.Equals(_options.Provider, "Android", StringComparison.OrdinalIgnoreCase)
        || string.Equals(_gatewayOptions.Provider, "Android", StringComparison.OrdinalIgnoreCase);

    private async Task<List<SmsRecipientResult>> SendThroughAndroidAsync(
        string message,
        IReadOnlyList<string> numbers,
        CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Max(1, _gatewayOptions.MaxConcurrency);
        var delayMs = Math.Max(0, _gatewayOptions.DelayBetweenMessagesMs);

        _logger.LogInformation(
            "Batch 1 started. Size={Size} Mode=Android MaxConcurrency={MaxConcurrency} DelayMs={DelayMs}",
            numbers.Count,
            maxConcurrency,
            delayMs);

        if (maxConcurrency == 1)
        {
            var sequential = new List<SmsRecipientResult>(numbers.Count);
            for (var i = 0; i < numbers.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sequential.Add(await SendOneAndroidAsync(numbers[i], message, cancellationToken));
                if (delayMs > 0 && i < numbers.Count - 1)
                    await Task.Delay(delayMs, cancellationToken);
            }

            _logger.LogInformation("Batch 1 completed.");
            return sequential;
        }

        var results = new SmsRecipientResult[numbers.Count];
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = numbers.Select(async (number, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                results[index] = await SendOneAndroidAsync(number, message, cancellationToken);
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Batch 1 completed.");
        return results.ToList();
    }

    private async Task<SmsRecipientResult> SendOneAndroidAsync(
        string number,
        string message,
        CancellationToken cancellationToken)
    {
        var requestId = SmsRequestIds.Next();
        _logger.LogInformation(
            "SMS started. RequestId={RequestId} RecipientSuffix={Suffix}",
            requestId,
            Suffix(number));

        var item = await SendWithRetryAsync(number, message, cancellationToken, requestId);

        if (item.Status == SmsDeliveryStatus.Sent)
        {
            _logger.LogInformation(
                "SMS completed. RequestId={RequestId} RecipientSuffix={Suffix}",
                requestId,
                Suffix(number));
        }
        else
        {
            _logger.LogWarning(
                "SMS failed. RequestId={RequestId} RecipientSuffix={Suffix} Error={Error}",
                requestId,
                Suffix(number),
                item.ErrorMessage);
        }

        return item;
    }

    private async Task<SmsRecipientResult> SendWithRetryAsync(
        string mobile,
        string message,
        CancellationToken cancellationToken,
        string? requestId = null)
    {
        var attempts = Math.Max(0, _options.RetryCount);
        ProviderSendResult? last = null;

        for (var attempt = 0; attempt <= attempts; attempt++)
        {
            if (requestId is not null && _smsProvider is IIdempotentSmsProvider tracked)
                last = await tracked.SendAsync(mobile, message, requestId, cancellationToken);
            else
                last = await _smsProvider.SendAsync(mobile, message, cancellationToken);
            if (last.Success)
            {
                return new SmsRecipientResult
                {
                    MobileNumber = mobile,
                    Status = SmsDeliveryStatus.Sent,
                    RequestId = requestId,
                    ProviderMessageId = last.ProviderMessageId,
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            if (!last.IsTransientFailure)
                break;

            if (attempt < attempts)
            {
                _logger.LogWarning(
                    "Transient provider error for ...{Suffix}. Attempt {Attempt}/{Max}. Status={Status}",
                    mobile.Length >= 4 ? mobile[^4..] : "****",
                    attempt + 1,
                    attempts + 1,
                    last.HttpStatusCode);

                await Task.Delay(_options.RetryDelayMs, cancellationToken);
            }
        }

        return new SmsRecipientResult
        {
            MobileNumber = mobile,
            Status = SmsDeliveryStatus.Failed,
            RequestId = requestId,
            ErrorMessage = last?.ErrorMessage ?? "Unknown provider error",
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static string Suffix(string mobile) =>
        mobile.Length <= 4 ? "****" : mobile[^4..];

    private static string BuildDedupeKey(string message, IReadOnlyList<string> numbers)
    {
        var joined = string.Join(',', numbers.OrderBy(n => n));
        return $"{message.GetHashCode(StringComparison.Ordinal)}|{joined.GetHashCode(StringComparison.Ordinal)}";
    }
}
