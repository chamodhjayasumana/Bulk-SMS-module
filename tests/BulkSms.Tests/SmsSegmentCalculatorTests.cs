using BulkSms.Application.Services;

namespace BulkSms.Tests;

public class SmsSegmentCalculatorTests
{
    private readonly SmsSegmentCalculator _sut = new();

    [Fact]
    public void Estimate_Gsm7_SingleSegment()
    {
        var result = _sut.Estimate(new string('A', 160), 10);
        Assert.False(result.IsUnicode);
        Assert.Equal(1, result.SegmentCount);
        Assert.Equal(10, result.EstimatedTotalSmsCount);
    }

    [Fact]
    public void Estimate_Gsm7_MultiSegment()
    {
        var result = _sut.Estimate(new string('A', 161), 2);
        Assert.False(result.IsUnicode);
        Assert.Equal(2, result.SegmentCount);
        Assert.Equal(4, result.EstimatedTotalSmsCount);
    }

    [Fact]
    public void Estimate_Unicode_Sinhala_Uses70Limit()
    {
        var sinhala = new string('අ', 70);
        var result = _sut.Estimate(sinhala, 5);
        Assert.True(result.IsUnicode);
        Assert.Equal(1, result.SegmentCount);
        Assert.Equal(5, result.EstimatedTotalSmsCount);
    }

    [Fact]
    public void Estimate_Unicode_MultiSegment()
    {
        var sinhala = new string('අ', 71);
        var result = _sut.Estimate(sinhala, 1);
        Assert.True(result.IsUnicode);
        Assert.Equal(2, result.SegmentCount);
    }
}
