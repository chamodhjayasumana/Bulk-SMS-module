namespace BulkSms.Domain.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "BulkSms";
    public string Audience { get; set; } = "BulkSms";
    public string Secret { get; set; } = "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_KEY_32+";
    public int ExpiryMinutes { get; set; } = 480;
}
