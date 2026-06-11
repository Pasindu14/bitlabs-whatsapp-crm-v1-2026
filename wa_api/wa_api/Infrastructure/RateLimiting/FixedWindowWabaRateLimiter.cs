using wa_api.Common.Errors;
using wa_api.Infrastructure.Caching;

namespace wa_api.Infrastructure.RateLimiting;

/// <summary>
/// Fixed one-minute window rate limiter per WABA phone-number id.
/// Bucket key: <c>rl:waba:{phoneNumberId}:{epochMinute}</c> — the minute number encodes the
/// window boundary so no sliding-window math is needed. Each key TTL is 120 s so Redis
/// auto-cleans the previous minute's key. Best-effort: Get+Set is not atomic, so bursts of
/// highly concurrent calls may overshoot by a few messages — acceptable for a protective guard.
/// Limit defaults to 80/min (Meta Tier-1) and is overridable via <c>RateLimit:WabaSendPerMinute</c>.
/// </summary>
public class FixedWindowWabaRateLimiter(ICacheService cache, IConfiguration config) : IWabaRateLimiter
{
    private readonly int _limit = config.GetValue<int>("RateLimit:WabaSendPerMinute", 80);

    public async Task CheckAsync(string phoneNumberId, CancellationToken ct = default)
    {
        var epochMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"rl:waba:{phoneNumberId}:{epochMinute}";

        var count = await cache.GetAsync<int?>(key, ct) ?? 0;
        if (count >= _limit)
            throw new RateLimitException();

        await cache.SetAsync(key, count + 1, TimeSpan.FromSeconds(120), ct);
    }
}
