namespace wa_api.Infrastructure.RateLimiting;

/// <summary>Why a permit was (not) granted.</summary>
public enum RateLimitReason
{
    Ok,
    /// <summary>The per-second token bucket is empty; wait <see cref="RateLimitResult.RetryAfter"/> and retry.</summary>
    PerSecondThrottle,
    /// <summary>The number's 24-hour unique-recipient tier cap is reached (Phase 2).</summary>
    DailyTierExceeded,
}

/// <summary>
/// Outcome of a single permit attempt. <see cref="RetryAfter"/> is the soonest a permit could free up
/// (zero when <see cref="Allowed"/>).
/// </summary>
public readonly record struct RateLimitResult(bool Allowed, TimeSpan RetryAfter, RateLimitReason Reason)
{
    public static readonly RateLimitResult Allow = new(true, TimeSpan.Zero, RateLimitReason.Ok);
}

/// <summary>
/// Paces outbound sends per WABA phone-number id so a tenant's number never exceeds Meta's per-second
/// throughput or its 24-hour business-initiated tier cap. Atomic across worker replicas (Redis token
/// bucket); degrades to a bounded in-process bucket on a cache outage — never fail-open.
/// </summary>
public interface IWabaRateLimiter
{
    /// <summary>
    /// Non-throwing. Attempts to consume one send permit for <paramref name="phoneNumberId"/>.
    /// </summary>
    /// <param name="recipientContactId">
    /// The recipient contact for <strong>business-initiated</strong> sends (campaigns), counted toward the
    /// 24-hour unique-recipient tier cap. Pass <c>null</c> for session/service replies (inbox within the
    /// 24-hour window) — those consume only the per-second bucket, not the tier.
    /// </param>
    /// <param name="dailyTierLimit">
    /// The number's daily unique-recipient cap. <c>&lt;= 0</c> skips the daily check (Phase 1, and all
    /// session messages).
    /// </param>
    Task<RateLimitResult> TryAcquireAsync(
        string phoneNumberId,
        Guid? recipientContactId,
        int dailyTierLimit,
        CancellationToken ct = default);
}
