using System.Text.RegularExpressions;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;

namespace BulkSms.Application.Services;

public partial class MobileNumberValidator : IMobileNumberValidator
{
    // Sri Lankan mobile prefixes (local 07X / international 947X)
    private static readonly HashSet<string> ValidLocalPrefixes = new()
    {
        "070", "071", "072", "074", "075", "076", "077", "078"
    };

    public NumberValidationItem Validate(string? raw)
    {
        var item = new NumberValidationItem { RawValue = raw ?? string.Empty };

        if (string.IsNullOrWhiteSpace(raw))
        {
            item.Reason = "Empty value";
            return item;
        }

        var digits = DigitsOnly(raw.Trim());

        if (digits.Length == 0)
        {
            item.Reason = "Empty value";
            return item;
        }

        // Local: 07XXXXXXXX (10 digits)
        if (digits.Length == 10 && digits.StartsWith('0'))
        {
            var prefix = digits[..3];
            if (!ValidLocalPrefixes.Contains(prefix))
            {
                item.Reason = IsLikelyLandline(prefix)
                    ? "Landline numbers are not allowed"
                    : "Invalid mobile prefix";
                return item;
            }

            item.IsValid = true;
            item.Normalized = "94" + digits[1..];
            return item;
        }

        // International without +: 947XXXXXXXXX (11 digits)
        if (digits.Length == 11 && digits.StartsWith("94"))
        {
            var localStyle = "0" + digits[2..];
            var prefix = localStyle[..3];
            if (!ValidLocalPrefixes.Contains(prefix))
            {
                item.Reason = IsLikelyLandline(prefix)
                    ? "Landline numbers are not allowed"
                    : "Invalid mobile prefix";
                return item;
            }

            item.IsValid = true;
            item.Normalized = digits;
            return item;
        }

        if (digits.Length < 10)
        {
            item.Reason = "Too-short number";
            return item;
        }

        if (digits.Length > 11)
        {
            item.Reason = "Too-long number";
            return item;
        }

        item.Reason = "Invalid number format";
        return item;
    }

    public ValidateNumbersResult ValidateMany(IEnumerable<string?> rawNumbers)
    {
        var result = new ValidateNumbersResult();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var samples = new List<InvalidNumberSample>();

        foreach (var raw in rawNumbers)
        {
            result.Total++;
            var item = Validate(raw);

            if (!item.IsValid)
            {
                result.Invalid++;
                if (samples.Count < 50)
                {
                    samples.Add(new InvalidNumberSample
                    {
                        RawValue = item.RawValue,
                        Reason = item.Reason ?? "Invalid"
                    });
                }
                continue;
            }

            if (!seen.Add(item.Normalized!))
            {
                result.Duplicates++;
                item.IsDuplicate = true;
                continue;
            }

            result.Numbers.Add(item.Normalized!);
        }

        result.Valid = result.Numbers.Count;
        result.InvalidSamples = samples;
        return result;
    }

    private static bool IsLikelyLandline(string prefix3) =>
        prefix3.StartsWith("011") || prefix3.StartsWith("021") || prefix3.StartsWith("031") ||
        prefix3.StartsWith("033") || prefix3.StartsWith("034") || prefix3.StartsWith("035") ||
        prefix3.StartsWith("036") || prefix3.StartsWith("037") || prefix3.StartsWith("038") ||
        prefix3.StartsWith("041") || prefix3.StartsWith("045") || prefix3.StartsWith("047") ||
        prefix3.StartsWith("051") || prefix3.StartsWith("052") || prefix3.StartsWith("054") ||
        prefix3.StartsWith("055") || prefix3.StartsWith("057") || prefix3.StartsWith("063") ||
        prefix3.StartsWith("065") || prefix3.StartsWith("066") || prefix3.StartsWith("067") ||
        prefix3.StartsWith("081") || prefix3.StartsWith("091");

    private static string DigitsOnly(string value)
    {
        // Keep leading + stripped; digits only for normalization
        var match = NonDigitRegex().Replace(value, string.Empty);
        return match;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();
}
