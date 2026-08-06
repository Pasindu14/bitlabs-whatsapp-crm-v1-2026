using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;
using wa_api.Features.Plans.Entities;

namespace wa_api.Features.Subscriptions.Entities;

/// <summary>
/// Whether a subscribe operation stacked onto a live subscription or started a fresh period.
/// </summary>
public enum SubscriptionPurchaseMode
{
    /// <summary>A brand-new period: no live sub, or the prior one was expired/exhausted.</summary>
    Fresh,

    /// <summary>Stacked onto a live subscription: balance added, expiry extended.</summary>
    Stack,
}

/// <summary>
/// An audit record of a single "subscribe" (the Assign action) against a company's
/// <see cref="Subscription"/>. One row per subscribe so the company's subscription history is
/// queryable even though STACK operations mutate the single active subscription row rather than
/// creating a new one. Values are SNAPSHOTTED at purchase time (<see cref="PlanName"/>,
/// <see cref="MessagesAdded"/>, <see cref="Price"/>, <see cref="BalanceAfter"/>,
/// <see cref="PeriodEndAfter"/>) so later catalog/subscription changes don't rewrite history.
/// Mirrors <see cref="Packages.Entities.PackagePurchase"/>.
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive from <see cref="BaseEntity"/>;
/// is tenant-scoped via <see cref="ITenantEntity.CompanyId"/>.
/// </para>
/// </summary>
public class SubscriptionPurchase : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The subscription this subscribe applied to (the resulting active row).</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>The catalog plan that was subscribed to. Restrict-deleted so history keeps its FK.</summary>
    public Guid PlanId { get; set; }

    /// <summary>Plan name at purchase time (snapshot).</summary>
    public string PlanName { get; set; } = null!;

    /// <summary>Whether this subscribe stacked onto a live sub or started fresh.</summary>
    public SubscriptionPurchaseMode Mode { get; set; }

    /// <summary>Messages granted by this subscribe (snapshot of the plan's monthly quota).</summary>
    public int MessagesAdded { get; set; }

    /// <summary>Days added to the subscription's validity by this subscribe.</summary>
    public int PeriodDays { get; set; }

    /// <summary>Remaining message balance right after this subscribe (snapshot).</summary>
    public int BalanceAfter { get; set; }

    /// <summary>The subscription's expiry (UTC) right after this subscribe (snapshot).</summary>
    public DateTime PeriodEndAfter { get; set; }

    /// <summary>Plan price at purchase time (snapshot).</summary>
    public decimal Price { get; set; }

    /// <summary>Currency at purchase time (snapshot).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// The PayHere order this purchase resulted from, when self-serve. Null for SuperAdmin
    /// manual assignments (and any future non-PayHere gateway) — the only signal distinguishing
    /// how a given row was purchased.
    /// </summary>
    public Guid? PayHereOrderId { get; set; }

    /// <summary>Owning company navigation.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>Applied-to subscription navigation.</summary>
    public Subscription Subscription { get; set; } = null!;

    /// <summary>Subscribed catalog plan navigation.</summary>
    public Plan Plan { get; set; } = null!;

    /// <summary>The PayHere order navigation, when <see cref="PayHereOrderId"/> is set.</summary>
    public PayHereOrder? PayHereOrder { get; set; }
}
