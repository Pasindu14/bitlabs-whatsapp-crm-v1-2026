namespace wa_api.Common.Subscriptions;

/// <summary>
/// Cross-cutting check that the caller's company holds an active, in-quota subscription
/// before a paid action (message send, campaign launch) is allowed. This is the PRD Phase 3.1
/// gate STUB — the full middleware (3.4) hardens it across more endpoints later.
/// </summary>
public interface ISubscriptionGate
{
    /// <summary>
    /// Throws when the caller's company has no active subscription, the subscription is
    /// inactive/expired, or its monthly quota is exhausted. Returns normally when sending is allowed.
    /// SuperAdmin (platform plane) bypasses the check.
    /// </summary>
    Task EnsureCanSendAsync(CancellationToken ct = default);

    /// <summary>
    /// Throws when the caller's company has no active subscription or the subscription is
    /// inactive/expired — but does NOT check the quota. For endpoints where the free-or-charged
    /// decision is made per send further in (a reply inside the open 24-hour customer-service window
    /// is free and must go through on an exhausted balance). Charged sends are still capped by
    /// <see cref="ISubscriptionMeter"/>, which remains the hard ceiling.
    /// SuperAdmin (platform plane) bypasses the check.
    /// </summary>
    Task EnsureActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Messages left in the caller company's current period (plan quota + extra credits − used), floored at 0.
    /// Never throws: returns 0 when there is no active, in-period subscription, and null for SuperAdmin
    /// (platform plane — no plan quota). Informational only (e.g. the campaign launch preview): a campaign may
    /// launch on a partial balance — it sends what fits and holds the rest as QUOTA_EXCEEDED — because the
    /// per-send <see cref="ISubscriptionMeter"/> reservation is what actually caps the send.
    /// </summary>
    Task<int?> GetRemainingQuotaAsync(CancellationToken ct = default);
}
