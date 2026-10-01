namespace BulkSms.Domain.Options;

/// <summary>
/// Local Android SMS gateway settings. The phone is reached only from ASP.NET.
/// The active sender remains <see cref="SmsProviderOptions.Provider"/> (Mock, Rest, or Android).
/// </summary>
public class SmsGatewayOptions
{
    public const string SectionName = "SmsGateway";

    public bool Enabled { get; set; } = true;

    /// <summary>Mock or Android. Mock never contacts the phone. Android uses the URL and token below.</summary>
    public string Provider { get; set; } = "Mock";

    public string AndroidGatewayUrl { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public int DelayBetweenMessagesMs { get; set; } = 1000;

    /// <summary>
    /// Parallel sends to the phone. Keep this at 1 so the SIM sends one message at a time.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;
}
