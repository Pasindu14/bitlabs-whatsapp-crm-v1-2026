using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace wa_api.Infrastructure.RateLimiting;

/// <summary>
/// <see cref="IWabaRateLimiter"/> for single-process / no-Redis deployments (dev). Uses the full configured
/// per-second rate because there is exactly one process. In multi-replica production Redis is configured and
/// <see cref="RedisTokenBucketRateLimiter"/> is used instead (this class still backs its outage fallback via
/// the shared <see cref="LocalBucketRegistry"/>).
/// </summary>
public sealed class InProcessTokenBucket(LocalBucketRegistry registry, IOptions<RateLimitOptions> options)
    : IWabaRateLimiter
{
    private readonly RateLimitOptions _opts = options.Value;

    // Per-(number, UTC day) set of recipient contact ids for the unique-recipient tier cap. Keyed by date so
    // stale days are simply never read again; negligible for a single dev process.
    private readonly ConcurrentDictionary<string, HashSet<Guid>> _dailyRecipients = new();

    public Task<RateLimitResult> TryAcquireAsync(
        string phoneNumberId, Guid? recipientContactId, int dailyTierLimit, CancellationToken ct = default)
    {
        var bucket = registry.TryTake($"tb:{phoneNumberId}", _opts.PerSecondCapacity, _opts.PerSecondRefill);
        if (!bucket.Allowed)
            return Task.FromResult(bucket);

        // Daily unique-recipient tier cap — business-initiated sends only (contact id + positive tier).
        if (recipientContactId is Guid contactId && dailyTierLimit > 0)
        {
            var dayKey = $"{phoneNumberId}:{DateTime.UtcNow:yyyyMMdd}";
            var set = _dailyRecipients.GetOrAdd(dayKey, _ => new HashSet<Guid>());
            lock (set)
            {
                if (!set.Contains(contactId))
                {
                    if (set.Count >= dailyTierLimit)
                        return Task.FromResult(
                            new RateLimitResult(false, TimeSpan.Zero, RateLimitReason.DailyTierExceeded));
                    set.Add(contactId);
                }
            }
        }

        return Task.FromResult(RateLimitResult.Allow);
    }

    public Task<int> GetDailyUsageAsync(string phoneNumberId, CancellationToken ct = default)
    {
        var dayKey = $"{phoneNumberId}:{DateTime.UtcNow:yyyyMMdd}";
        if (_dailyRecipients.TryGetValue(dayKey, out var set))
            lock (set) return Task.FromResult(set.Count);
        return Task.FromResult(0);
    }
}
