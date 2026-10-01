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
    private readonly ILogger<BulkSmsService> _logger;

    // Simple in-process guard against accidental double submission of identical payloads
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RecentSendKeys = new();

    public BulkSmsService(
        IMobileNumberValidator validator,
        IRecipientFileParser fileParser,
        ISmsSegmentCalculator segmentCalculator,
        ISmsProvider smsProvider,
        IOptions<SmsProviderOptions> options,
        ILogger<BulkSmsService> logger)
    {
        _validator = validator;
        _fileParser = fileParser;
        _segmentCalculator = segmentCalculator;
        _smsProvider = smsProvider;
        _options = options.Value;
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

        _logger.LogInformation(
            "Bulk SMS send started. Recipients={Count} MessageLength={Length} Provider={Provider}",
            validated.Valid, request.Message.Length, _options.Provider);

        var results = new ConcurrentBag<SmsRecipientResult>();
        var batchSize = Math.Max(1, _options.BatchSize);
        var maxConcurrency = Math.Max(1, _options.MaxConcurrency);
        var numbers = validated.Numbers;
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

        var list = results.ToList();
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

    private async Task<SmsRecipientResult> SendWithRetryAsync(string mobile, string message, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(0, _options.RetryCount);
        ProviderSendResult? last = null;

        for (var attempt = 0; attempt <= attempts; attempt++)
        {
            last = await _smsProvider.SendAsync(mobile, message, cancellationToken);
            if (last.Success)
            {
                return new SmsRecipientResult
                {
                    MobileNumber = mobile,
                    Status = SmsDeliveryStatus.Sent,
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
            ErrorMessage = last?.ErrorMessage ?? "Unknown provider error",
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static string BuildDedupeKey(string message, IReadOnlyList<string> numbers)
    {
        var joined = string.Join(',', numbers.OrderBy(n => n));
        return $"{message.GetHashCode(StringComparison.Ordinal)}|{joined.GetHashCode(StringComparison.Ordinal)}";
    }
}
