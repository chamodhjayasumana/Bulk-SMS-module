namespace BulkSms.Application.Services;

/// <summary>
/// In-memory ids of the form BULK-yyyyMMdd-000001. The same id is reused for retries of one SMS.
/// </summary>
public static class SmsRequestIds
{
    private static readonly object Gate = new();
    private static string _day = string.Empty;
    private static int _sequence;

    public static string Next()
    {
        var day = DateTime.Now.ToString("yyyyMMdd");
        lock (Gate)
        {
            if (!string.Equals(_day, day, StringComparison.Ordinal))
            {
                _day = day;
                _sequence = 0;
            }

            _sequence++;
            return $"BULK-{day}-{_sequence:000000}";
        }
    }
}
