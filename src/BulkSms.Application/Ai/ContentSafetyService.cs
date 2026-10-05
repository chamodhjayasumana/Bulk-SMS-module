using System.Text.RegularExpressions;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;

namespace BulkSms.Application.Ai;

/// <summary>
/// Server-side checks for generated SMS copy. Findings use fixed wording so secrets in the text are not repeated.
/// </summary>
public sealed class ContentSafetyService : IContentSafetyService
{
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled);
    private static readonly Regex SecretRegex = new(
        @"(?i)\b(password|api[_-]?key|secret|bearer)\b\s*(is|=|:)\s*\S+|sk-[A-Za-z0-9]{10,}",
        RegexOptions.Compiled);
    private static readonly Regex OtpRegex = new(
        @"(?i)\b(otp|pin|verification code|one-time code)\b.{0,24}\d{4,8}\b",
        RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(
        @"(?i)\b(https?://|www\.|bit\.ly/|tinyurl\.com/|t\.co/)\S+",
        RegexOptions.Compiled);
    private static readonly string[] SpamPhrases =
    {
        "you have won", "click here", "free money", "act now", "100% free"
    };
    private static readonly string[] AbusivePhrases =
    {
        "kill yourself", "bomb threat", "i will hurt you"
    };

    private readonly ISmsSegmentCalculator _segments;

    public ContentSafetyService(ISmsSegmentCalculator segments)
    {
        _segments = segments;
    }

    public bool IsSeverelyUnsafe(string? text) =>
        InspectMessage(text, Array.Empty<string>(), maxSegments: 5).HasSevere;

    public SafetyReport InspectMessage(string? message, IReadOnlyCollection<string> allowedPlaceholders, int maxSegments)
    {
        var text = message ?? string.Empty;
        var estimate = _segments.Estimate(text, 1);
        var allowed = new HashSet<string>(allowedPlaceholders ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var found = PlaceholderRegex.Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var report = new SafetyReport
        {
            CharacterCount = estimate.CharacterCount,
            Segments = estimate.SegmentCount,
            Placeholders = found
        };

        if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetterOrDigit))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "empty",
                Message = "Message is empty or has no readable text."
            });
        }

        var letters = text.Count(char.IsLetter);
        var uppercase = text.Count(c => char.IsLetter(c) && char.IsUpper(c));
        if (letters >= 12 && uppercase >= letters * 0.7)
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "uppercase",
                Message = "Message uses too much uppercase text."
            });
        }

        var punctuation = text.Count(char.IsPunctuation);
        if (text.Contains("!!!", StringComparison.Ordinal) || text.Contains("???", StringComparison.Ordinal) ||
            (text.Length >= 12 && punctuation >= text.Length * 0.25))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "punctuation",
                Message = "Message uses excessive punctuation."
            });
        }

        if (UrlRegex.IsMatch(text))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "url",
                Message = "Message contains a URL. Check that the link is expected."
            });
        }

        if (SpamPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "spam",
                Message = "Message looks like spam."
            });
        }

        if (SecretRegex.IsMatch(text))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "secret",
                Message = "Message looks like it contains a password, API key, or secret.",
                Severe = true
            });
        }

        if (OtpRegex.IsMatch(text))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "otp",
                Message = "Message looks like a one-time password.",
                Severe = true
            });
        }

        if (AbusivePhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "abusive",
                Message = "Message looks harmful or abusive.",
                Severe = true
            });
        }

        foreach (var placeholder in found)
        {
            if (!allowed.Contains(placeholder))
            {
                report.Findings.Add(new SafetyFinding
                {
                    Code = "placeholder",
                    Message = $"Placeholder {{{placeholder}}} is not allowed."
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(text) && estimate.SegmentCount > Math.Max(1, maxSegments))
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "segments",
                Message = $"Message uses {estimate.SegmentCount} SMS segments. The maximum is {maxSegments}."
            });
        }

        if (text.Length > 1000)
        {
            report.Findings.Add(new SafetyFinding
            {
                Code = "length",
                Message = "Message is excessively long."
            });
        }

        return report;
    }
}
