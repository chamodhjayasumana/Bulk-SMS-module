namespace BulkSms.Domain.Options;

public class AuthUserOptions
{
    public const string SectionName = "AuthUsers";

    public List<AuthUser> Users { get; set; } = new();
}

public class AuthUser
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Must be BulkSMS for bulk endpoints.</summary>
    public string AccessType { get; set; } = "BulkSMS";
}
