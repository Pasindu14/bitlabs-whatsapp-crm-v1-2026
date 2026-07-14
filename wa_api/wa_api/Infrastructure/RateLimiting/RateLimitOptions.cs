namespace wa_api.Infrastructure.RateLimiting;

/// <summary>
/// Bound from configuration section <c>"RateLimit"</c>. All values are deliberately conservative and well
/// under Meta's Cloud-API ceiling — confirm Meta's live per-second / per-tier limits before raising them.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Token-bucket burst ceiling (max permits available at once), per phone number.</summary>
    public double PerSecondCapacity { get; set; } = 20;

    /// <summary>Sustained refill rate in permits/second, per phone number.</summary>
    public double PerSecondRefill { get; set; } = 20;

    /// <summary>
    /// Per-replica refill rate used by the in-process fallback when Redis is unavailable. Kept low so the
    /// worst case across R replicas (R × this) still stays under Meta's ceiling.
    /// </summary>
    public double FallbackRefill { get; set; } = 5;

    /// <summary>Sliding TTL (seconds) on the bucket hash so idle numbers self-clean.</summary>
    public int BucketTtlSeconds { get; set; } = 120;

    /// <summary>TTL (seconds) on the per-day unique-recipient Set (Phase 2). ~25 h rolls off next day.</summary>
    public int DailySetTtlSeconds { get; set; } = 90000;

    /// <summary>Interactive (HTTP) wait budget before returning 429.</summary>
    public int InteractiveMaxWaitMs { get; set; } = 2000;

    /// <summary>Max time a campaign worker may block waiting for a permit before rescheduling the batch.</summary>
    public int CampaignWaitCapMs { get; set; } = 750;

    /// <summary>
    /// Burst smoothing: after roughly this many successful campaign sends, the next batch is chained with a
    /// brief pause (<see cref="BurstPauseMs"/>) instead of immediately. This breaks a number's continuous
    /// send stream into gentler bursts, which eases Meta's quality heuristics on business-initiated sends. It
    /// does NOT lower the send rate — the per-second token bucket owns that; it only spaces the bursts out.
    /// Counted against the campaign's cumulative sent count, so it is batch-size independent. Set to 0 to disable.
    /// </summary>
    public int BurstPauseEverySends { get; set; } = 100;

    /// <summary>Length of the burst-smoothing pause inserted at a batch-chain boundary (see
    /// <see cref="BurstPauseEverySends"/>). Kept small — enough to break the stream, not slow the campaign.</summary>
    public int BurstPauseMs { get; set; } = 2500;
}
