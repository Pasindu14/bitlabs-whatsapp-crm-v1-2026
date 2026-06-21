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
}
