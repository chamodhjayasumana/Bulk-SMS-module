namespace BulkSms.Domain.Models;

/// <summary>Natural-language request for SMS copy. This does not send any SMS.</summary>
public class CampaignDraftRequest
{
    public string CampaignDescription { get; set; } = string.Empty;

    /// <summary>en, si, or ta.</summary>
    public string Language { get; set; } = "en";

    public string? SenderName { get; set; }

    public int MaxSegments { get; set; } = 2;

    /// <summary>professional, friendly, urgent, or promotional.</summary>
    public string Tone { get; set; } = "professional";

    public bool IncludeCallToAction { get; set; }

    /// <summary>Placeholder names the model may use, such as name or orderNumber.</summary>
    public List<string> PersonalizationFields { get; set; } = new();
}

/// <summary>One reviewed suggestion. The caller must still confirm before any send.</summary>
public class CampaignSuggestion
{
    public string Message { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int Segments { get; set; }
    public string Tone { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public List<string> Placeholders { get; set; } = new();
}

/// <summary>Draft result. <see cref="RequiresReview"/> is always true.</summary>
public class CampaignDraftResponse
{
    public List<CampaignSuggestion> Suggestions { get; set; } = new();
    public List<string> SafetyWarnings { get; set; } = new();
    public bool RequiresReview { get; set; } = true;
}

/// <summary>Inputs passed to an <c>IAiProvider</c>. Contains no API key.</summary>
public class AiGenerationRequest
{
    public string CampaignDescription { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string SenderName { get; set; } = string.Empty;
    public string Tone { get; set; } = "professional";
    public bool IncludeCallToAction { get; set; }
    public IReadOnlyList<string> AllowedPlaceholders { get; set; } = Array.Empty<string>();
    public int SuggestionCount { get; set; } = 3;
    public int MaxSegments { get; set; } = 2;
}

/// <summary>One safety finding. Severe findings can be blocked by configuration.</summary>
public class SafetyFinding
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Severe { get; set; }
}

/// <summary>Length, placeholder, and safety result for one message.</summary>
public class SafetyReport
{
    public int CharacterCount { get; set; }
    public int Segments { get; set; }
    public List<string> Placeholders { get; set; } = new();
    public List<SafetyFinding> Findings { get; set; } = new();

    public bool HasSevere => Findings.Exists(f => f.Severe);
}
