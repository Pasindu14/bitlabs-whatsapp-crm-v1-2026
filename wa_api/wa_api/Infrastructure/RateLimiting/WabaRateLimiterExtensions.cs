using wa_api.Common.Errors;

namespace wa_api.Infrastructure.RateLimiting;

public static class WabaRateLimiterExtensions
{
    /// <summary>
    /// Interactive (HTTP) waiting policy: poll for a permit, waiting out short per-second throttles up to
    /// <paramref name="maxWait"/>. Throws <see cref="RateLimitException"/> (→ 429) if a permit can't be had in
    /// time, or immediately on a daily-tier rejection. Single interactive sends almost always get a permit at once.
    /// </summary>
    public static async Task AcquireOrThrowAsync(
        this IWabaRateLimiter limiter,
        string phoneNumberId,
        Guid? recipientContactId,
        int dailyTierLimit,
        TimeSpan maxWait,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + maxWait;
        while (true)
        {
            var result = await limiter.TryAcquireAsync(phoneNumberId, recipientContactId, dailyTierLimit, ct);
            if (result.Allowed) return;

            var remaining = deadline - DateTime.UtcNow;
            if (result.Reason == RateLimitReason.DailyTierExceeded
                || remaining <= TimeSpan.Zero
                || result.RetryAfter >= remaining)
            {
                throw new RateLimitException();
            }

            await Task.Delay(result.RetryAfter, ct);
        }
    }
}
