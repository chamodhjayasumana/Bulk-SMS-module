using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSms.Application.Providers;

/// <summary>
/// Configurable REST SMS gateway adapter.
///
/// *** PROVIDER-SPECIFIC MAPPING — UPDATE HERE ***
/// Adjust BuildRequestPayload / ParseResponse to match your SMS gateway documentation.
/// Field names are driven by SMSProvider:* options so you can change many mappings without code changes.
/// </summary>
public class RestSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly SmsProviderOptions _options;
    private readonly ILogger<RestSmsProvider> _logger;

    public RestSmsProvider(HttpClient httpClient, IOptions<SmsProviderOptions> options, ILogger<RestSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProviderSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
        {
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = "SMS provider ApiUrl is not configured",
                IsTransientFailure = false
            };
        }

        try
        {
            // --- PROVIDER-SPECIFIC: request body ---
            var payload = BuildRequestPayload(mobileNumber, message);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(new HttpMethod(_options.HttpMethod), _options.ApiUrl)
            {
                Content = content
            };

            ApplyAuth(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // --- PROVIDER-SPECIFIC: response mapping ---
            return ParseResponse(response.StatusCode, body);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "SMS provider timeout for recipient ending {Suffix}", Suffix(mobileNumber));
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = "Provider timeout",
                IsTransientFailure = true
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "SMS provider network failure for recipient ending {Suffix}", Suffix(mobileNumber));
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = "Network failure contacting SMS provider",
                IsTransientFailure = true
            };
        }
    }

    /// <summary>
    /// PROVIDER-SPECIFIC: Build the JSON body your gateway expects.
    /// Default shape: { "to": "...", "text": "...", "from": "..." }
    /// Also includes username when configured (some gateways require it in body).
    /// </summary>
    private string BuildRequestPayload(string mobileNumber, string message)
    {
        var doc = new Dictionary<string, object?>
        {
            [_options.RequestMobileField] = mobileNumber,
            [_options.RequestMessageField] = message,
            [_options.RequestSenderField] = _options.SenderId
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
            doc["username"] = _options.Username;

        // Do NOT put password/apiKey in logs. Some providers need password in body — configure carefully.
        if (!string.IsNullOrWhiteSpace(_options.Password))
            doc["password"] = _options.Password;

        return JsonSerializer.Serialize(doc);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        // Prefer ApiKey as Bearer (or custom header). Username/password may also be used by Basic auth if no ApiKey.
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            if (string.Equals(_options.AuthHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    string.IsNullOrWhiteSpace(_options.AuthScheme) ? "Bearer" : _options.AuthScheme,
                    _options.ApiKey);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(_options.AuthHeaderName, _options.ApiKey);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    /// <summary>
    /// PROVIDER-SPECIFIC: Interpret gateway response into Success / MessageId / Error.
    /// </summary>
    private ProviderSendResult ParseResponse(HttpStatusCode statusCode, string body)
    {
        var code = (int)statusCode;
        var transient = code == 408 || code == 429 || code >= 500;

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ProviderSendResult
            {
                Success = statusCode == HttpStatusCode.OK || statusCode == HttpStatusCode.Accepted,
                ErrorMessage = statusCode == HttpStatusCode.OK || statusCode == HttpStatusCode.Accepted
                    ? null
                    : $"Provider returned {(int)statusCode}",
                IsTransientFailure = transient,
                HttpStatusCode = code
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var success = statusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.Created;
            if (root.TryGetProperty(_options.ResponseSuccessField, out var successEl))
            {
                success = successEl.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(successEl.GetString(), out var b) && b,
                    JsonValueKind.Number => successEl.GetInt32() == 1,
                    _ => success
                };
            }

            string? messageId = null;
            if (root.TryGetProperty(_options.ResponseMessageIdField, out var idEl))
                messageId = idEl.ToString();

            string? error = null;
            if (root.TryGetProperty(_options.ResponseErrorField, out var errEl))
                error = errEl.ToString();

            if (!success && string.IsNullOrWhiteSpace(error))
                error = $"Provider rejected message ({code})";

            return new ProviderSendResult
            {
                Success = success,
                ProviderMessageId = messageId,
                ErrorMessage = error,
                IsTransientFailure = !success && transient,
                HttpStatusCode = code
            };
        }
        catch (JsonException)
        {
            var ok = statusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.Created;
            return new ProviderSendResult
            {
                Success = ok,
                ErrorMessage = ok ? null : "Invalid API response",
                IsTransientFailure = !ok && transient,
                HttpStatusCode = code
            };
        }
    }

    private static string Suffix(string mobile) =>
        mobile.Length <= 4 ? "****" : mobile[^4..];
}
