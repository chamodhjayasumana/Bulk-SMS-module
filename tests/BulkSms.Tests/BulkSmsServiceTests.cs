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
        SmsProviderOptions? options = null)
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
}
