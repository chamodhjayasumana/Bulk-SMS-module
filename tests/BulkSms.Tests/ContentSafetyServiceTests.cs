using BulkSms.Application.Ai;
using BulkSms.Application.Services;

namespace BulkSms.Tests;

public class ContentSafetyServiceTests
{
    private readonly ContentSafetyService _sut = new(new SmsSegmentCalculator());

    [Fact]
    public void WarnsAboutUppercasePunctuationUrlAndSpam()
    {
        var report = _sut.InspectMessage(
            "CLICK HERE NOW!!! YOU HAVE WON https://X.TEST",
            Array.Empty<string>(),
            2);

        Assert.Contains(report.Findings, f => f.Code == "uppercase");
        Assert.Contains(report.Findings, f => f.Code == "punctuation");
        Assert.Contains(report.Findings, f => f.Code == "url");
        Assert.Contains(report.Findings, f => f.Code == "spam");
        Assert.DoesNotContain(report.Findings, f => f.Severe);
    }

    [Fact]
    public void BlocksSecretsAndOtpWithoutRepeatingTheSecret()
    {
        var secret = _sut.InspectMessage("Your password is hunter2", Array.Empty<string>(), 2);
        var otp = _sut.InspectMessage("Your OTP is 123456", Array.Empty<string>(), 2);

        Assert.Contains(secret.Findings, f => f.Severe && f.Code == "secret");
        Assert.Contains(otp.Findings, f => f.Severe && f.Code == "otp");
        Assert.DoesNotContain("hunter2", secret.Findings[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", otp.Findings.Single(f => f.Code == "otp").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlocksAbusiveText()
    {
        var report = _sut.InspectMessage("I will hurt you", Array.Empty<string>(), 2);
        Assert.Contains(report.Findings, f => f.Severe && f.Code == "abusive");
    }
}
