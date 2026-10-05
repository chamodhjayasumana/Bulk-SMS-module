using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BulkSms.Application.Ai;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSms.Application.Providers;

/// <summary>
/// Calls an OpenAI-compatible chat completions endpoint. The API key is sent only as a bearer header and is never logged.
/// </summary>
public sealed class OpenAiCompatibleAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleAiProvider> _logger;

    public OpenAiCompatibleAiProvider(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleAiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "OpenAI";

    public async Task<IReadOnlyList<string>> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl) || string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiProviderException("The external AI provider is not configured.");

        var attempts = Math.Max(1, _options.RetryCount + 1);
        AiProviderException? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await SendOnceAsync(request, cancellationToken);
            }
            catch (AiProviderException ex) when (ex.IsTransient && attempt < attempts)
            {
                last = ex;
                _logger.LogWarning("AI provider attempt {Attempt} failed with a transient error.", attempt);
                if (_options.RetryDelayMs > 0)
                    await Task.Delay(_options.RetryDelayMs, cancellationToken);
            }
        }

        throw last ?? new AiProviderException("The AI provider could not complete the request.");
    }

    private async Task<IReadOnlyList<string>> SendOnceAsync(AiGenerationRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl.Trim());
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        httpRequest.Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("The AI provider timed out.", isTimeout: true, isTransient: true);
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("AI provider request failed before a response.");
            throw new AiProviderException("The AI provider could not be reached.", isTransient: true);
        }

        using (response)
        {
            if (IsTransient(response.StatusCode))
            {
                _logger.LogWarning("AI provider returned transient status {StatusCode}.", (int)response.StatusCode);
                throw new AiProviderException("The AI provider could not complete the request.", isTransient: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI provider returned status {StatusCode}.", (int)response.StatusCode);
                throw new AiProviderException("The AI provider could not complete the request.");
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseSuggestions(body);
        }
    }

    private string BuildBody(AiGenerationRequest request)
    {
        var placeholders = request.AllowedPlaceholders.Count == 0
            ? "Do not use placeholders."
            : "Only these placeholders are allowed: " + string.Join(", ", request.AllowedPlaceholders.Select(p => "{" + p + "}")) + ".";
        var system =
            "You write SMS campaign text for human review. Return JSON only, shaped as {\"suggestions\":[\"...\"]}. " +
            $"Write exactly {request.SuggestionCount} suggestions in language code {request.Language}. " +
            $"Tone is {request.Tone}. Keep each message within {request.MaxSegments} SMS segments. " +
            placeholders + " Do not include passwords, API keys, or one-time codes.";
        var user = string.IsNullOrWhiteSpace(request.SenderName)
            ? request.CampaignDescription
            : $"Sender name: {request.SenderName}. Campaign: {request.CampaignDescription}";
        if (request.IncludeCallToAction)
            user += " Include a short call to action.";

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o-mini" : _options.Model,
            temperature = 0.4,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyList<string> ParseSuggestions(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
            content = content.Trim();
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var start = content.IndexOf('\n');
                var end = content.LastIndexOf("```", StringComparison.Ordinal);
                if (start >= 0 && end > start)
                    content = content[(start + 1)..end].Trim();
            }

            using var inner = JsonDocument.Parse(content);
            if (!inner.RootElement.TryGetProperty("suggestions", out var suggestions) ||
                suggestions.ValueKind != JsonValueKind.Array)
                throw new JsonException();

            var list = suggestions.EnumerateArray()
                .Select(item => item.GetString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToList();
            if (list.Count == 0)
                throw new JsonException();
            return list;
        }
        catch (AiProviderException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AiProviderException("The AI provider returned an unexpected response.");
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
