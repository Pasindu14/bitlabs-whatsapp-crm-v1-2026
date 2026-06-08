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
}
