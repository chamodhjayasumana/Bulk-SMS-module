using BulkSms.Application.Interfaces;

namespace BulkSms.Application.Ai;

/// <summary>In-memory per-user draft limit. Resets when the API process restarts.</summary>
public sealed class AiRequestGuard : IAiRequestGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTime>> _hits = new(StringComparer.Ordinal);

    public void Check(string userKey, int limitPerMinute)
    {
        if (limitPerMinute <= 0)
            return;

        var key = string.IsNullOrWhiteSpace(userKey) ? "anonymous" : userKey.Trim();
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_hits.TryGetValue(key, out var hits))
            {
                hits = new Queue<DateTime>();
                _hits[key] = hits;
            }

            while (hits.Count > 0 && (now - hits.Peek()).TotalSeconds >= 60)
                hits.Dequeue();

            if (hits.Count >= limitPerMinute)
                throw new AiRateLimitException("Too many AI draft requests. Wait a minute and try again.");

            hits.Enqueue(now);
        }
    }
}
