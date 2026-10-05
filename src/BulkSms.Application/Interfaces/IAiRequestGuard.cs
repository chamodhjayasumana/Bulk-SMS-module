namespace BulkSms.Application.Interfaces;

/// <summary>Limits how often one signed-in user can ask for a campaign draft.</summary>
public interface IAiRequestGuard
{
    void Check(string userKey, int limitPerMinute);
}
