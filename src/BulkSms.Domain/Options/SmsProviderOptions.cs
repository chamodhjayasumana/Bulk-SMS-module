namespace BulkSms.Domain.Options;

public class SmsProviderOptions
{
    public const string SectionName = "SMSProvider";

    /// <summary>Mock | Rest</summary>
    public string Provider { get; set; } = "Mock";

    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;

    public int BatchSize { get; set; } = 50;
    public int MaxConcurrency { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 500;

    public string HttpMethod { get; set; } = "POST";
    public string AuthHeaderName { get; set; } = "Authorization";
    public string AuthScheme { get; set; } = "Bearer";

    /// <summary>
    /// PROVIDER-SPECIFIC: JSON property names expected by your gateway.
    /// Adjust these (and RestSmsProvider mapping) to match provider docs.
    /// </summary>
    public string RequestMobileField { get; set; } = "to";
    public string RequestMessageField { get; set; } = "text";
    public string RequestSenderField { get; set; } = "from";
    public string ResponseMessageIdField { get; set; } = "messageId";
    public string ResponseSuccessField { get; set; } = "success";
    public string ResponseErrorField { get; set; } = "error";

    public int MaxRecipientsPerRequest { get; set; } = 5000;
    public long MaxUploadBytes { get; set; } = 5_000_000;
    public int LargeBatchConfirmThreshold { get; set; } = 100;
}
