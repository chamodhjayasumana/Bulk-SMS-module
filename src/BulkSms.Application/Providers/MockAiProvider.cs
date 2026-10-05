using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;

namespace BulkSms.Application.Providers;

/// <summary>
/// Deterministic campaign copy for development and tests. It does not call an external AI service.
/// </summary>
public sealed class MockAiProvider : IAiProvider
{
    public string Name => "Mock";

    public Task<IReadOnlyList<string>> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Clamp(request.SuggestionCount, 1, 5);
        var messages = new string[count];
        for (var i = 0; i < count; i++)
            messages[i] = Build(request, i);
        return Task.FromResult<IReadOnlyList<string>>(messages);
    }

    private static string Build(AiGenerationRequest request, int index)
    {
        var sender = string.IsNullOrWhiteSpace(request.SenderName) ? string.Empty : request.SenderName.Trim() + ":";
        var topic = Trim(request.CampaignDescription.Trim(), 48);
        var greeting = Has(request, "name") ? Greeting(request.Language) : string.Empty;
        var extras = request.AllowedPlaceholders
            .Where(p => !p.Equals("name", StringComparison.OrdinalIgnoreCase))
            .Select(p => "{" + p + "}");
        var extra = string.Join(" ", extras);
        var lead = Lead(request.Language, request.Tone, index);
        var cta = request.IncludeCallToAction ? CallToAction(request.Language, request.Tone) : string.Empty;
        return string.Join(" ", new[] { sender, greeting, lead, topic, extra, cta }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static bool Has(AiGenerationRequest request, string field) =>
        request.AllowedPlaceholders.Any(p => p.Equals(field, StringComparison.OrdinalIgnoreCase));

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd();

    private static string Greeting(string language) => language switch
    {
        "si" => "ආයුබෝවන් {name}.",
        "ta" => "வணக்கம் {name}.",
        _ => "Hello {name}."
    };

    private static string Lead(string language, string tone, int index)
    {
        if (language == "si")
        {
            return index switch
            {
                1 => "විශේෂ දැනුම්දීම.",
                2 => tone == "urgent" ? "අද පමණයි." : "ඔබ වෙනුවෙන්.",
                _ => "පණිවිඩය."
            };
        }

        if (language == "ta")
        {
            return index switch
            {
                1 => "சிறப்பு அறிவிப்பு.",
                2 => tone == "urgent" ? "இன்று மட்டும்." : "உங்களுக்காக.",
                _ => "செய்தி."
            };
        }

        return index switch
        {
            1 => tone == "friendly" ? "A note from us." : "An update for you.",
            2 => tone == "promotional" ? "A special offer." : "Please read this.",
            _ => tone == "urgent" ? "Time-sensitive update." : "Campaign message."
        };
    }

    private static string CallToAction(string language, string tone)
    {
        if (language == "si")
            return tone == "urgent" ? "අදම පිළිතුරු දෙන්න." : "විස්තර සඳහා පිළිතුරු දෙන්න.";
        if (language == "ta")
            return tone == "urgent" ? "இன்று பதிலளியுங்கள்." : "விவரங்களுக்கு பதிலளியுங்கள்.";
        return tone switch
        {
            "friendly" => "Come visit us.",
            "urgent" => "Reply today.",
            "promotional" => "Ask us for the offer.",
            _ => "Reply YES for details."
        };
    }
}
