using System.Collections.Concurrent;
using BulkSms.Application.Interfaces;
using BulkSms.Domain.Models;

namespace BulkSms.Application.Providers;

/// <summary>
/// Fake SMS provider for development/testing. Does not call any external API.
/// Rules:
/// - numbers ending with 0 → permanent failure
/// - numbers ending with 9 → first attempt rate-limited (429), then success
/// - all others → success
/// </summary>
public class MockSmsProvider : ISmsProvider
{
    private static readonly ConcurrentDictionary<string, int> Attempts = new();

    public async Task<ProviderSendResult> SendAsync(string mobileNumber, string message, CancellationToken cancellationToken = default)
    {
        await Task.Delay(15, cancellationToken);

        if (string.IsNullOrWhiteSpace(mobileNumber))
        {
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = "Invalid number",
                IsTransientFailure = false
            };
        }

        var last = mobileNumber[^1];

        if (last == '0')
        {
            return new ProviderSendResult
            {
                Success = false,
                ErrorMessage = "Mock provider permanent failure",
                IsTransientFailure = false,
                HttpStatusCode = 400
            };
        }

        if (last == '9')
        {
            var attempt = Attempts.AddOrUpdate(mobileNumber, 1, (_, prev) => prev + 1);
            if (attempt == 1)
            {
                return new ProviderSendResult
                {
                    Success = false,
                    ErrorMessage = "Mock provider rate limited",
                    IsTransientFailure = true,
                    HttpStatusCode = 429
                };
            }
        }

        return new ProviderSendResult
        {
            Success = true,
            ProviderMessageId = $"MOCK-{Guid.NewGuid():N}"[..20],
            HttpStatusCode = 200
        };
    }

    public static void Reset() => Attempts.Clear();
}
