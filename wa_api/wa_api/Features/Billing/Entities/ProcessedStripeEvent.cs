namespace wa_api.Features.Billing.Entities;

/// <summary>
/// Dedup ledger for Stripe webhook events. Stripe delivers at-least-once (and our job carries an
/// AutomaticRetry budget), so the same event id can arrive more than once. Recording each processed
/// <c>StripeEventId</c> — with a UNIQUE index — lets <c>StripeWebhookProcessingJob</c> skip a replay
/// instead of re-applying its effects (e.g. re-zeroing <c>MessagesUsedThisPeriod</c>, which previously
/// wiped a period's usage on every redelivery). The marker is written in the SAME transaction as the
/// event's effects, so a mid-processing failure rolls both back and a genuine retry re-applies cleanly.
/// </summary>
public class ProcessedStripeEvent
{
    /// <summary>Stripe's event id (evt_...). Primary key — the natural dedup key.</summary>
    public string StripeEventId { get; set; } = string.Empty;

    /// <summary>The Stripe event type, for diagnostics.</summary>
    public string EventType { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }
}
