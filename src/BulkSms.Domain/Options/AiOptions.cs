namespace BulkSms.Domain.Options;

/// <summary>
/// Server-side AI campaign assistant settings. API keys stay in this configuration and are never sent to Angular.
/// </summary>
public class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>When false, an external provider is not called. Mock still works.</summary>
    public bool Enabled { get; set; }

    /// <summary>Mock or OpenAI. Mock never calls the network.</summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>OpenAI-compatible chat completions URL. Empty when using Mock.</summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>Bearer token for the external provider. Never log or return this value.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many suggestions to ask for. Clamped by <see cref="HardMaxSuggestions"/>.</summary>
    public int MaxSuggestions { get; set; } = 3;

    /// <summary>Absolute cap so configuration cannot request an unbounded list.</summary>
    public int HardMaxSuggestions { get; set; } = 5;

    public int MaxPromptLength { get; set; } = 2000;

    /// <summary>When true, suggestions with severe safety findings are dropped.</summary>
    public bool BlockSevereFindings { get; set; } = true;

    public int RetryCount { get; set; } = 1;

    public int RetryDelayMs { get; set; } = 200;

    /// <summary>Draft requests allowed per signed-in user each minute. Zero disables the guard.</summary>
    public int RequestsPerMinute { get; set; } = 8;
}
