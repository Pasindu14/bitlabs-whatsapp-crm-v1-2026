using wa_api.Common.Errors;
using wa_api.Infrastructure.Caching;

namespace wa_api.Infrastructure.RateLimiting;

/// <summary>
/// Fixed one-minute window rate limiter per WABA phone-number id.
/// Bucket key: <c>rl:waba:{phoneNumberId}:{epochMinute}</c> — the minute number encodes the
/// window boundary so no sliding-window math is needed. Each key TTL is 120 s so Redis
/// auto-cleans the previous minute's key. Uses an atomic <see cref="ICacheService.IncrementAsync"/>
/// (Redis <c>INCR</c>, or a process-wide gate for the memory cache) so concurrent bursts can't each
/// read a stale count and slip past the limit.
/// Limit defaults to 80/min (Meta Tier-1) and is overridable via <c>RateLimit:WabaSendPerMinute</c>.
/// </summary>
public class FixedWindowWabaRateLimiter(ICacheService cache, IConfiguration config) : IWabaRateLimiter
{
    private readonly int _limit = config.GetValue<int>("RateLimit:WabaSendPerMinute", 80);

    public async Task CheckAsync(string phoneNumberId, CancellationToken ct = default)
    {
        var epochMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"rl:waba:{phoneNumberId}:{epochMinute}";

        // Increment-then-check: the atomic increment reserves this call's slot in one step, so the
        // (limit+1)-th caller in the window is the first to see a value over the limit and is rejected.
        var count = await cache.IncrementAsync(key, TimeSpan.FromSeconds(120), ct);
        if (count > _limit)
            throw new RateLimitException();
    }
}
