using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;
using wa_api.Features.Plans.Entities;

namespace wa_api.Features.Subscriptions.Entities;

/// <summary>
/// Lifecycle status of a company's subscription. No Stripe-specific states (e.g. PastDue)
/// in this slice — assignment is manual (SuperAdmin), so the source of truth is local.
/// </summary>
public enum SubscriptionStatus { Active, Inactive, Trialing, Cancelled }

/// <summary>
/// A tenant <see cref="Company"/>'s placement on a platform <see cref="Plan"/>, with the
/// usage counter for the current period. Assigned/changed ONLY by SuperAdmin in this phase
/// (PRD Phase 3.1, Stripe deferred). A company holds AT MOST ONE <see cref="SubscriptionStatus.Active"/>
/// subscription — enforced by a unique filtered index in <c>AppDbContext</c>.
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>;
/// is tenant-scoped via <see cref="ITenantEntity.CompanyId"/>.
/// </para>
/// </summary>
public class Subscription : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The plan tier this company is subscribed to.</summary>
    public Guid PlanId { get; set; }

    /// <summary>Current lifecycle status.</summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>UTC start of the current usage period.</summary>
    public DateTime CurrentPeriodStart { get; set; }

    /// <summary>
    /// UTC end of the current usage period. Once this passes the subscription is expired and
    /// sending is blocked until the company subscribes again. There is no automatic monthly
    /// reset — the balance runs continuously until this date (accumulate-until-expiry model).
    /// </summary>
    public DateTime CurrentPeriodEnd { get; set; }

    /// <summary>Messages consumed against the effective quota in the current period.</summary>
    public int MessagesUsedThisPeriod { get; set; }

    /// <summary>
    /// Extra message credits stacked on top of the plan's
    /// <see cref="Plans.Entities.Plan.MonthlyMessageQuota"/> to form the effective quota. Grows
    /// when a company re-subscribes onto a LIVE subscription (the new plan's quota is folded in
    /// here so the running balance rises by exactly the new quota) and when SuperAdmin adds an
    /// add-on <see cref="Packages.Entities.MessagePackage"/>. There is no monthly reset — the
    /// balance runs continuously until <see cref="CurrentPeriodEnd"/>.
    /// </summary>
    public int ExtraMessageCredits { get; set; }

    /// <summary>
    /// Stripe Subscription ID (<c>sub_xxx</c>). Null for manually-assigned subscriptions.
    /// Unique among active rows so the webhook job can do an O(1) lookup.
    /// </summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Owning company navigation.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>Subscribed plan navigation.</summary>
    public Plan Plan { get; set; } = null!;
}
