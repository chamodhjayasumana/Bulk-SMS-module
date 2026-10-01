using System.Net;
using System.Text;
using BulkSms.Application.Providers;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BulkSms.Tests;

public class AndroidSmsGatewayServiceTests
{
    [Fact]
    public async Task GetStatus_ParsesOnlineDialogGateway()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {
              "online": true,
              "networkAvailable": true,
              "simOperator": "Dialog",
              "ipAddress": "192.168.1.25",
              "port": 8080,
              "smsPermissionGranted": true
            }
            """));

        var sut = Create(handler, "secret-token");
        var status = await sut.GetStatusAsync();

        Assert.True(status.Online);
        Assert.True(status.NetworkAvailable);
        Assert.Equal("Dialog", status.SimOperator);
        Assert.Equal("192.168.1.25", status.IpAddress);
        Assert.Equal(8080, status.Port);
        Assert.Null(status.Error);
        Assert.Equal("Bearer secret-token", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.EndsWith("/api/gateway/status", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetStatus_WhenPhoneUnreachable_ReturnsOffline()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = Create(handler, "secret-token");

        var status = await sut.GetStatusAsync();

        Assert.False(status.Online);
        Assert.Equal(AndroidSmsGatewayService.OfflineMessage, status.Error);
        Assert.DoesNotContain("secret-token", status.Error);
    }

    [Fact]
    public async Task GetStatus_WhenTokenRejected_DoesNotExposeToken()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = Create(handler, "secret-token");

        var status = await sut.GetStatusAsync();

        Assert.False(status.Online);
        Assert.Equal(AndroidSmsGatewayService.TokenRejectedMessage, status.Error);
    }

    [Fact]
    public async Task Send_PostsRequestIdAndTreatsDuplicateFailureAsFinal()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            { "requestId": "BULK-20261001-000001", "success": false, "duplicate": true, "error": "SMS sending failed" }
            """));

        var sut = Create(handler, "secret-token");
        var result = await sut.SendAsync("94771234567", "Hello", "BULK-20261001-000001");

        Assert.False(result.Success);
        Assert.False(result.IsTransientFailure);
        Assert.Equal("SMS sending failed", result.ErrorMessage);
        Assert.Contains("\"requestId\":\"BULK-20261001-000001\"", handler.LastBody);
        Assert.Contains("\"phoneNumber\":\"94771234567\"", handler.LastBody);
        Assert.Equal("Bearer secret-token", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.DoesNotContain("secret-token", handler.LastBody);
    }

    [Fact]
    public async Task Send_SuccessDoesNotRequireARealNetwork()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            { "requestId": "BULK-20261001-000002", "success": true }
            """));

        var sut = Create(handler, "secret-token");
        var result = await sut.SendAsync("94771234567", "Hello", "BULK-20261001-000002");

        Assert.True(result.Success);
        Assert.Equal("BULK-20261001-000002", result.ProviderMessageId);
    }

    private static AndroidSmsGatewayService Create(StubHandler handler, string token)
    {
        var client = new HttpClient(handler);
        return new AndroidSmsGatewayService(
            client,
            Options.Create(new SmsGatewayOptions
            {
                Enabled = true,
                AndroidGatewayUrl = "http://192.168.1.25:8080",
                ApiToken = token,
                TimeoutSeconds = 5
            }),
            NullLogger<AndroidSmsGatewayService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }
}
