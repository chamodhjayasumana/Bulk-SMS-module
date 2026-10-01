using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BulkSms.Application.Interfaces;
using BulkSms.Application.Services;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSms.Application.Providers;

/// <summary>
/// Talks to the local Android SMS gateway over Wi-Fi. Does not send when the active provider is Mock.
/// </summary>
public class AndroidSmsGatewayService : IAndroidSmsGateway, IIdempotentSmsProvider
{
    public const string OfflineMessage = "Android gateway is offline.";
    public const string TimeoutMessage = "Android gateway timeout.";
    public const string TokenRejectedMessage = "Invalid gateway token.";
    public const string PermissionDeniedMessage = "SMS permission denied.";
    public const string UrlMissingMessage = "Android gateway URL is not configured.";
    public const string TokenMissingMessage = "Android gateway token is not configured.";
    public const string DisabledMessage = "SMS gateway is disabled.";
    public const string StoppedMessage = "Android gateway is stopped.";
    public const string WifiDisconnectedMessage = "Wi-Fi disconnected.";
    public const string InvalidResponseMessage = "Android gateway returned an invalid response.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly SmsGatewayOptions _options;
    private readonly ILogger<AndroidSmsGatewayService> _logger;

    public AndroidSmsGatewayService(
        HttpClient httpClient,
        IOptions<SmsGatewayOptions> options,
        ILogger<AndroidSmsGatewayService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ProviderSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken = default)
    {
        return SendAsync(mobileNumber, message, SmsRequestIds.Next(), cancellationToken);
    }

    public async Task<ProviderSendResult> SendAsync(
        string mobileNumber,
        string message,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var blocked = ValidateReady();
        if (blocked is not null)
        {
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = blocked,
                ProviderMessageId = requestId,
                IsTransientFailure = false
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/api/sms/send"))
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        requestId,
                        phoneNumber = mobileNumber,
                        message
                    }, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            ApplyAuth(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseSendResponse(response.StatusCode, body, requestId);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Android gateway timeout. RequestId={RequestId} RecipientSuffix={Suffix}", requestId, Suffix(mobileNumber));
            return Transient(TimeoutMessage, requestId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Android gateway unreachable. RequestId={RequestId} RecipientSuffix={Suffix}", requestId, Suffix(mobileNumber));
            return Transient(OfflineMessage, requestId);
        }
    }

    public async Task<SmsGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Gateway disconnected. Reason={Reason}", DisabledMessage);
            return Offline(DisabledMessage);
        }

        if (string.IsNullOrWhiteSpace(_options.AndroidGatewayUrl))
        {
            _logger.LogInformation("Gateway disconnected. Reason={Reason}", UrlMissingMessage);
            return Offline(UrlMissingMessage);
        }

        if (string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            _logger.LogInformation("Gateway disconnected. Reason={Reason}", TokenMissingMessage);
            return Offline(TokenMissingMessage);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("/api/gateway/status"));
            ApplyAuth(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Gateway disconnected. Reason={Reason}", TokenRejectedMessage);
                return Offline(TokenRejectedMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gateway disconnected. HttpStatus={Status}", (int)response.StatusCode);
                return Offline(OfflineMessage);
            }

            var status = ParseStatus(body);
            if (status.Online && status.NetworkAvailable && string.IsNullOrWhiteSpace(status.Error))
            {
                _logger.LogInformation(
                    "Gateway connected. Sim={Sim} Address={Address} Port={Port}",
                    status.SimOperator,
                    status.IpAddress,
                    status.Port);
            }
            else
            {
                _logger.LogWarning("Gateway disconnected. Reason={Reason}", status.Error);
            }

            return status;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gateway disconnected. Reason={Reason}", TimeoutMessage);
            return Offline(TimeoutMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gateway disconnected. Reason={Reason}", OfflineMessage);
            return Offline(OfflineMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gateway disconnected. Reason={Reason}", InvalidResponseMessage);
            return Offline(InvalidResponseMessage);
        }
    }

    private string? ValidateReady()
    {
        if (!_options.Enabled)
            return DisabledMessage;
        if (string.IsNullOrWhiteSpace(_options.AndroidGatewayUrl))
            return UrlMissingMessage;
        if (string.IsNullOrWhiteSpace(_options.ApiToken))
            return TokenMissingMessage;
        return null;
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = _options.AndroidGatewayUrl.Trim().TrimEnd('/');
        return new Uri(baseUrl + path, UriKind.Absolute);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken.Trim());
    }

    private static ProviderSendResult ParseSendResponse(HttpStatusCode statusCode, string body, string requestId)
    {
        var code = (int)statusCode;

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = TokenRejectedMessage,
                ProviderMessageId = requestId,
                IsTransientFailure = false,
                HttpStatusCode = code
            };
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = ReadError(body) ?? PermissionDeniedMessage,
                ProviderMessageId = requestId,
                IsTransientFailure = false,
                HttpStatusCode = code
            };
        }

        var transient = code == 408 || code == 429 || code >= 500;
        if (string.IsNullOrWhiteSpace(body))
        {
            var ok = statusCode is HttpStatusCode.OK or HttpStatusCode.Accepted;
            return new ProviderSendResult
            {
                Success = ok,
                ErrorMessage = ok ? null : OfflineMessage,
                ProviderMessageId = requestId,
                IsTransientFailure = !ok && transient,
                HttpStatusCode = code
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True;
            var duplicate = root.TryGetProperty("duplicate", out var duplicateEl) && duplicateEl.ValueKind == JsonValueKind.True;
            var error = ReadError(root);

            if (!success && string.IsNullOrWhiteSpace(error))
                error = "SMS sending failed.";

            return new ProviderSendResult
            {
                Success = success,
                ErrorMessage = success ? null : error,
                ProviderMessageId = requestId,
                IsTransientFailure = !success && transient && !duplicate,
                HttpStatusCode = code
            };
        }
        catch (JsonException)
        {
            var ok = statusCode is HttpStatusCode.OK or HttpStatusCode.Accepted;
            return new ProviderSendResult
            {
                Success = ok,
                ErrorMessage = ok ? null : InvalidResponseMessage,
                ProviderMessageId = requestId,
                IsTransientFailure = !ok && transient,
                HttpStatusCode = code
            };
        }
    }

    private static SmsGatewayStatus ParseStatus(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var online = root.TryGetProperty("online", out var onlineEl) && onlineEl.ValueKind == JsonValueKind.True;
            var network = root.TryGetProperty("networkAvailable", out var networkEl) && networkEl.ValueKind == JsonValueKind.True;
            var permissionGranted = !root.TryGetProperty("smsPermissionGranted", out var permissionEl)
                || permissionEl.ValueKind != JsonValueKind.False;

            string? error = null;
            if (!network)
                error = WifiDisconnectedMessage;
            else if (!online)
                error = StoppedMessage;
            else if (!permissionGranted)
                error = PermissionDeniedMessage;

            return new SmsGatewayStatus
            {
                Online = online && network,
                NetworkAvailable = network,
                SimOperator = root.TryGetProperty("simOperator", out var simEl) ? simEl.ToString() : string.Empty,
                IpAddress = root.TryGetProperty("ipAddress", out var ipEl) ? ipEl.ToString() : null,
                Port = root.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var port) ? port : null,
                Error = error
            };
        }
        catch (JsonException)
        {
            return Offline(InvalidResponseMessage);
        }
    }

    private static string? ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return ReadError(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var errorEl))
            return null;

        var value = errorEl.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static ProviderSendResult Transient(string message, string requestId) =>
        new()
        {
            Success = false,
            ErrorMessage = message,
            ProviderMessageId = requestId,
            IsTransientFailure = true
        };

    private static SmsGatewayStatus Offline(string error) =>
        new()
        {
            Online = false,
            NetworkAvailable = false,
            Error = error
        };

    private static string Suffix(string mobile) =>
        mobile.Length <= 4 ? "****" : mobile[^4..];
}
