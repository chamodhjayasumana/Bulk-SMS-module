using BulkSms.Application.Interfaces;
using BulkSms.Application.Providers;
using BulkSms.Application.Services;
using BulkSms.Domain.Enums;
using BulkSms.Domain.Models;
using BulkSms.Domain.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BulkSms.Tests;

public class BulkSmsServiceTests
{
    private static BulkSmsService CreateService(
        ISmsProvider? provider = null,
        SmsProviderOptions? options = null,
        SmsGatewayOptions? gatewayOptions = null)
    {
        options ??= new SmsProviderOptions
        {
            BatchSize = 10,
            MaxConcurrency = 3,
            RetryCount = 2,
            RetryDelayMs = 1,
            LargeBatchConfirmThreshold = 100,
            MaxRecipientsPerRequest = 5000
        };

        return new BulkSmsService(
            new MobileNumberValidator(),
            Mock.Of<IRecipientFileParser>(),
            new SmsSegmentCalculator(),
            provider ?? new MockSmsProvider(),
            Options.Create(options),
            Options.Create(gatewayOptions ?? new SmsGatewayOptions()),
            new OnlineGateway(),
            NullLogger<BulkSmsService>.Instance);
    }

    [Fact]
    public async Task SendBulk_PartialFailures()
    {
        MockSmsProvider.Reset();
        var sut = CreateService();

        var result = await sut.SendBulkAsync(new BulkSendRequest
        {
            Message = "Test",
            Confirmed = true,
            Recipients = new List<string> { "0712345671", "0712345670", "0771234567" }
        });

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Successful);
        Assert.Equal(1, result.Failed);
        Assert.Contains(result.Results, r => r.Status == SmsDeliveryStatus.Failed && r.MobileNumber.EndsWith('0'));
    }

    [Fact]
    public async Task SendBulk_RetriesRateLimitThenSucceeds()
    {
        MockSmsProvider.Reset();
        var sut = CreateService();

        var result = await sut.SendBulkAsync(new BulkSendRequest
        {
            Message = "Rate limit test " + Guid.NewGuid(),
            Confirmed = true,
            Recipients = new List<string> { "0712345679" }
        });

        Assert.Equal(1, result.Successful);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task SendBulk_RejectsDuplicateSubmit()
    {
        MockSmsProvider.Reset();
        var sut = CreateService();
        var request = new BulkSendRequest
        {
            Message = "Dedupe-" + Guid.NewGuid(),
            Confirmed = true,
            Recipients = new List<string> { "0712345671" }
        };

        await sut.SendBulkAsync(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendBulkAsync(request));
    }

    [Fact]
    public async Task SendBulk_RequiresConfirmationForLargeBatch()
    {
        var sut = CreateService(options: new SmsProviderOptions
        {
            LargeBatchConfirmThreshold = 2,
            RetryDelayMs = 1,
            BatchSize = 10,
            MaxConcurrency = 2
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SendBulkAsync(new BulkSendRequest
        {
            Message = "Large",
            Confirmed = false,
            Recipients = new List<string> { "0712345671", "0771234567", "0761234567" }
        }));
    }

    [Fact]
    public async Task SendBulk_ProviderTimeout_MarkedFailedAfterRetries()
    {
        var provider = new Mock<ISmsProvider>();
        provider.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSendResult
            {
                Success = false,
                ErrorMessage = "Provider timeout",
                IsTransientFailure = true,
                HttpStatusCode = 408
            });

        var sut = CreateService(provider.Object, new SmsProviderOptions
        {
            RetryCount = 1,
            RetryDelayMs = 1,
            BatchSize = 5,
            MaxConcurrency = 2,
            LargeBatchConfirmThreshold = 100
        });

        var result = await sut.SendBulkAsync(new BulkSendRequest
        {
            Message = "Timeout " + Guid.NewGuid(),
            Confirmed = true,
            Recipients = new List<string> { "0712345671" }
        });

        Assert.Equal(1, result.Failed);
        Assert.Equal("Provider timeout", result.Results[0].ErrorMessage);
        provider.Verify(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SendBulk_AndroidMode_SendsOneAtATimeWithStableRequestIds()
    {
        var provider = new RecordingGatewayProvider();
        var sut = CreateService(
            provider,
            new SmsProviderOptions
            {
                Provider = "Android",
                RetryCount = 1,
                RetryDelayMs = 1,
                BatchSize = 10,
                MaxConcurrency = 5,
                LargeBatchConfirmThreshold = 100
            },
            new SmsGatewayOptions
            {
                DelayBetweenMessagesMs = 40,
                MaxConcurrency = 1
            });

        var result = await sut.SendBulkAsync(new BulkSendRequest
        {
            Message = "Android sequence " + Guid.NewGuid(),
            Confirmed = true,
            Recipients = new List<string> { "0712345671", "0771234567" }
        });

        Assert.Equal(2, result.Successful);
        Assert.Equal(2, provider.Calls.Count);
        Assert.All(provider.Calls, call => Assert.Matches(@"^BULK-\d{8}-\d{6}$", call.RequestId));
        Assert.NotEqual(provider.Calls[0].RequestId, provider.Calls[1].RequestId);
        Assert.True(provider.Calls[1].StartedAt >= provider.Calls[0].FinishedAt.AddMilliseconds(30));
        Assert.Equal(provider.Calls[0].RequestId, result.Results[0].RequestId);
        Assert.Equal("94712345671", result.Results[0].MobileNumber);
        Assert.Equal("94771234567", result.Results[1].MobileNumber);
    }

    [Fact]
    public async Task SendBulk_AndroidMode_StopsWhenGatewayIsOffline()
    {
        var provider = new RecordingGatewayProvider();
        var offline = new BulkSmsService(
            new MobileNumberValidator(),
            Mock.Of<IRecipientFileParser>(),
            new SmsSegmentCalculator(),
            provider,
            Options.Create(new SmsProviderOptions { Provider = "Android", LargeBatchConfirmThreshold = 100 }),
            Options.Create(new SmsGatewayOptions { Provider = "Android" }),
            new OnlineGateway { Online = false, Error = "Android gateway is offline." },
            NullLogger<BulkSmsService>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => offline.SendBulkAsync(new BulkSendRequest
        {
            Message = "Should not send " + Guid.NewGuid(),
            Confirmed = true,
            Recipients = new List<string> { "0712345671" }
        }));

        Assert.Equal("Android gateway is offline.", ex.Message);
        Assert.Empty(provider.Calls);
    }

    private sealed class OnlineGateway : IAndroidSmsGateway
    {
        public bool Online { get; set; } = true;
        public string? Error { get; set; }

        public Task<SmsGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmsGatewayStatus
            {
                Online = Online,
                NetworkAvailable = Online,
                SimOperator = "Dialog",
                Error = Error
            });

        public Task<ProviderSendResult> SendAsync(
            string mobileNumber,
            string message,
            string requestId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingGatewayProvider : IIdempotentSmsProvider
    {
        public List<RecordedCall> Calls { get; } = new();

        public Task<ProviderSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken = default)
            => SendAsync(mobileNumber, message, "missing", cancellationToken);

        public async Task<ProviderSendResult> SendAsync(
            string mobileNumber,
            string message,
            string requestId,
            CancellationToken cancellationToken = default)
        {
            var started = DateTime.UtcNow;
            await Task.Delay(20, cancellationToken);
            Calls.Add(new RecordedCall(mobileNumber, requestId, started, DateTime.UtcNow));
            return new ProviderSendResult
            {
                Success = true,
                ProviderMessageId = requestId,
                HttpStatusCode = 200
            };
        }
    }

    private sealed record RecordedCall(string MobileNumber, string RequestId, DateTime StartedAt, DateTime FinishedAt);
}
