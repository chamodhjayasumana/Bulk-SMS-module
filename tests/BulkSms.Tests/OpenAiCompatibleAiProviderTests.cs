using System.Net;
using System.Text;
using BulkSms.Application.Ai;
using BulkSms.Application.Providers;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkSms.Tests;

public class OpenAiCompatibleAiProviderTests
{
    private const string SecretKey = "sk-test-DO-NOT-LEAK";

    [Fact]
    public async Task Timeout_ThrowsSafeTimeoutError()
    {
        var provider = Create(new StallHandler(), new AiOptions
        {
            ApiUrl = "https://ai.example/v1/chat/completions",
            ApiKey = SecretKey,
            TimeoutSeconds = 1,
            RetryCount = 0
        }, out var logger);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Prompt()));
        Assert.True(ex.IsTimeout);
        Assert.DoesNotContain(SecretKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Lines, line => line.Contains(SecretKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedResponse_ThrowsSafeError()
    {
        var provider = Create(new ScriptHandler(_ => JsonResponse("{\"choices\":[{\"message\":{\"content\":\"not-json\"}}]}")), new AiOptions
        {
            ApiUrl = "https://ai.example/v1/chat/completions",
            ApiKey = SecretKey,
            RetryCount = 0,
            TimeoutSeconds = 5
        }, out _);

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Prompt()));
        Assert.Contains("unexpected", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransientFailure_IsRetriedThenParsed()
    {
        var calls = 0;
        var provider = Create(new ScriptHandler(_ =>
        {
            calls++;
            if (calls == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return JsonResponse("{\"choices\":[{\"message\":{\"content\":\"{\\\"suggestions\\\":[\\\"Hello shop\\\"]}\"}}]}");
        }), new AiOptions
        {
            ApiUrl = "https://ai.example/v1/chat/completions",
            ApiKey = SecretKey,
            RetryCount = 1,
            RetryDelayMs = 0,
            TimeoutSeconds = 5
        }, out var logger);

        var result = await provider.GenerateAsync(Prompt());

        Assert.Equal(2, calls);
        Assert.Equal("Hello shop", Assert.Single(result));
        Assert.DoesNotContain(logger.Lines, line => line.Contains(SecretKey, StringComparison.Ordinal));
    }

    private static OpenAiCompatibleAiProvider Create(HttpMessageHandler handler, AiOptions options, out ListLogger logger)
    {
        logger = new ListLogger();
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new OpenAiCompatibleAiProvider(client, Options.Create(options), logger);
    }

    private static AiGenerationRequest Prompt() => new()
    {
        CampaignDescription = "Promote our weekend discount",
        Language = "en",
        Tone = "professional",
        SuggestionCount = 1,
        MaxSegments = 2
    };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StallHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("delay should have been cancelled");
        }
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _next;
        public ScriptHandler(Func<HttpRequestMessage, HttpResponseMessage> next) => _next = next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var auth = request.Headers.Authorization?.Parameter;
            if (auth != SecretKey)
                throw new InvalidOperationException("Authorization header was not the configured key.");
            return Task.FromResult(_next(request));
        }
    }

    private sealed class ListLogger : ILogger<OpenAiCompatibleAiProvider>
    {
        public List<string> Lines { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
