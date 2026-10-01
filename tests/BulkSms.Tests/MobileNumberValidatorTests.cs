using BulkSms.Application.Services;

namespace BulkSms.Tests;

public class MobileNumberValidatorTests
{
    private readonly MobileNumberValidator _sut = new();

    [Theory]
    [InlineData("0712345678", "94712345678")]
    [InlineData("0771234567", "94771234567")]
    [InlineData("0761234567", "94761234567")]
    [InlineData("0781234567", "94781234567")]
    [InlineData("94712345678", "94712345678")]
    [InlineData("+94771234567", "94771234567")]
    [InlineData("94 771 234 567", "94771234567")]
    public void Validate_NormalizesValidSriLankanMobiles(string input, string expected)
    {
        var result = _sut.Validate(input);
        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_RejectsEmpty(string? input)
    {
        var result = _sut.Validate(input);
        Assert.False(result.IsValid);
        Assert.Equal("Empty value", result.Reason);
    }

    [Theory]
    [InlineData("0112345678")]
    [InlineData("0212345678")]
    public void Validate_RejectsLandlines(string input)
    {
        var result = _sut.Validate(input);
        Assert.False(result.IsValid);
        Assert.Contains("Landline", result.Reason!);
    }

    [Theory]
    [InlineData("071234567")]   // too short
    [InlineData("07123456789")] // too long local
    [InlineData("947123456789")] // too long intl
    [InlineData("0731234567")]  // invalid prefix
    public void Validate_RejectsInvalidFormats(string input)
    {
        var result = _sut.Validate(input);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateMany_RemovesDuplicates()
    {
        var result = _sut.ValidateMany(new[]
        {
            "0712345678",
            "94712345678",
            "+94712345678",
            "0771234567"
        });

        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.Valid);
        Assert.Equal(2, result.Duplicates);
        Assert.Equal(0, result.Invalid);
        Assert.Contains("94712345678", result.Numbers);
        Assert.Contains("94771234567", result.Numbers);
    }
}
