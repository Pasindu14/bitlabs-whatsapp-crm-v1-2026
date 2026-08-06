using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;
using wa_api.Features.Plans.Entities;

namespace wa_api.Features.Subscriptions.Entities;

/// <summary>Lifecycle of a single PayHere checkout attempt.</summary>
public enum PayHereOrderStatus
{
    /// <summary>Created, awaiting the PayHere notify callback.</summary>
    Pending,

    /// <summary>Payment succeeded (status_code 2) — the subscription was assigned.</summary>
    Completed,

    /// <summary>Customer cancelled/dismissed the checkout (status_code -1).</summary>
    Cancelled,

    /// <summary>Payment failed (status_code -2).</summary>
    Failed,

    /// <summary>Payment was later charged back (status_code -3).</summary>
    ChargedBack,
}

/// <summary>
/// A single PayHere checkout attempt for a company buying a <see cref="Plan"/>. Created with
/// <see cref="PayHereOrderStatus.Pending"/> BEFORE the customer is sent to PayHere, so the notify
/// webhook (which carries no long-lived customer object, unlike Stripe) has a row to correlate
/// against by <c>order_id</c> — this entity's own <see cref="BaseEntity.Id"/> IS the PayHere
/// <c>order_id</c>, so no separate order-id column is needed.
/// <para>
/// <see cref="Status"/> doubles as the webhook idempotency ledger: PayHere may redeliver its
/// notify POST, and the processing job only applies effects once, guarded by
/// <c>Status == Pending</c> inside the same transaction that flips it to a terminal state.
/// </para>
/// <para>
/// Written by the webhook job (no HTTP tenant context) for the terminal-state update, so
/// <see cref="CompanyId"/> is stamped EXPLICITLY at creation time — not via the AuditInterceptor.
/// </para>
/// </summary>
public class PayHereOrder : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    public Guid PlanId { get; set; }

    /// <summary>Snapshot of Plan.Price at order time — PayHere's checkout amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Snapshot of Plan.Currency at order time (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    public PayHereOrderStatus Status { get; set; } = PayHereOrderStatus.Pending;

    /// <summary>PayHere's payment_id, set once the notify callback arrives.</summary>
    public string? PayHerePaymentId { get; set; }

    /// <summary>PayHere's status_message from the notify callback, for diagnostics.</summary>
    public string? StatusMessage { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>The subscription this order resulted in, once applied.</summary>
    public Guid? SubscriptionId { get; set; }

    public Company Company { get; set; } = null!;

    public Plan Plan { get; set; } = null!;
}
