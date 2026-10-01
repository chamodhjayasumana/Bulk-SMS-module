using System.Net;
using System.Text;
using BulkSms.Application.Providers;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BulkSms.Tests;

public class RestSmsProviderTests
{
    private static RestSmsProvider CreateProvider(HttpMessageHandler handler, SmsProviderOptions? opts = null)
    {
        opts ??= new SmsProviderOptions
        {
            ApiUrl = "https://sms.example/send",
            ApiKey = "test-key",
            SenderId = "TEST"
        };

        var client = new HttpClient(handler);
        return new RestSmsProvider(client, Options.Create(opts), NullLogger<RestSmsProvider>.Instance);
    }

    [Fact]
    public async Task SendAsync_ParsesSuccessResponse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true,"messageId":"MID-1"}""", Encoding.UTF8, "application/json")
        });

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync("94771234567", "Hello");

        Assert.True(result.Success);
        Assert.Equal("MID-1", result.ProviderMessageId);
    }

    [Fact]
    public async Task SendAsync_MarksRateLimitAsTransient()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("""{"success":false,"error":"rate limited"}""", Encoding.UTF8, "application/json")
        });

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync("94771234567", "Hello");

        Assert.False(result.Success);
        Assert.True(result.IsTransientFailure);
        Assert.Equal(429, result.HttpStatusCode);
    }

    [Fact]
    public async Task SendAsync_MarksTimeoutAsTransient()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        var provider = CreateProvider(handler);
        var result = await provider.SendAsync("94771234567", "Hello");

        Assert.False(result.Success);
        Assert.True(result.IsTransientFailure);
        Assert.Equal("Provider timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_HandlesInvalidJsonAsFailureWhenNonSuccessStatus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        });

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync("94771234567", "Hello");

        Assert.False(result.Success);
        Assert.Equal("Invalid API response", result.ErrorMessage);
        Assert.False(result.IsTransientFailure);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
