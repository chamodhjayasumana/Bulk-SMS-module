using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;

namespace BulkSms.Application.Services;

/// <summary>
/// Estimates SMS segments for GSM-7 vs Unicode (UCS-2) messages.
/// Limits: GSM-7 single 160 / concatenated 153; Unicode single 70 / concatenated 67.
/// </summary>
public class SmsSegmentCalculator : ISmsSegmentCalculator
{
    private static readonly HashSet<char> Gsm7Basic = new(
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ ÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà");

    private static readonly HashSet<char> Gsm7Extended = new("^{}\\[~]|€");

    public MessageEstimate Estimate(string message, int recipientCount)
    {
        message ??= string.Empty;
        var isUnicode = !IsGsm7(message);
        var charCount = isUnicode ? message.Length : CountGsm7Septets(message);
        var segments = CalculateSegments(charCount, isUnicode);

        return new MessageEstimate
        {
            CharacterCount = message.Length,
            SegmentCount = segments,
            IsUnicode = isUnicode,
            RecipientCount = recipientCount,
            EstimatedTotalSmsCount = segments * Math.Max(0, recipientCount)
        };
    }

    private static bool IsGsm7(string message)
    {
        foreach (var c in message)
        {
            if (!Gsm7Basic.Contains(c) && !Gsm7Extended.Contains(c))
                return false;
        }
        return true;
    }

    private static int CountGsm7Septets(string message)
    {
        var count = 0;
        foreach (var c in message)
        {
            count += Gsm7Extended.Contains(c) ? 2 : 1;
        }
        return count;
    }

    private static int CalculateSegments(int units, bool unicode)
    {
        if (units <= 0) return 0;

        if (!unicode)
        {
            if (units <= 160) return 1;
            return (int)Math.Ceiling(units / 153.0);
        }

        if (units <= 70) return 1;
        return (int)Math.Ceiling(units / 67.0);
    }
}
