using wa_api.Common.Entities;

namespace wa_api.Features.Plans.Entities;

/// <summary>
/// A subscription tier in the shared platform catalog. PLATFORM-LEVEL: like
/// <see cref="Companies.Company"/> this is NOT tenant-scoped — it carries no
/// <c>CompanyId</c> and is not covered by the global query filter. Managed ONLY by
/// SuperAdmin; companies are attached to a plan via a <see cref="Subscriptions.Entities.Subscription"/>.
/// <para>
/// Stripe is intentionally absent in this slice (PRD Phase 3.1): <see cref="Price"/> +
/// <see cref="Currency"/> are display-only; a Stripe <c>PriceId</c> is added later (PRD 3.2).
/// </para>
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>.
/// </para>
/// </summary>
public class Plan : BaseEntity
{
    /// <summary>Display name. Unique platform-wide (e.g. "Free", "Starter", "Pro").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Messages a company on this plan may send per billing period.</summary>
    public int MonthlyMessageQuota { get; set; }

    /// <summary>Display price per period. Not charged in this slice (no Stripe yet).</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code for <see cref="Price"/>.</summary>
    public string Currency { get; set; } = "AED";

    /// <summary>
    /// Capability keys (from <see cref="Auth.Permission"/>) this plan unlocks. Stored as a
    /// Postgres <c>text[]</c> (same pattern as <c>User.Permissions</c>). Reusing the permission
    /// catalog lets the subscription gate map plan → allowed capabilities with no new vocabulary.
    /// </summary>
    public List<string> FeatureFlags { get; set; } = [];

    /// <summary>
    /// Stripe Price ID (e.g. <c>price_xxx</c>) for self-service checkout. Null for manual-only
    /// plans (assigned by SuperAdmin without Stripe). Required for <c>POST /my-subscription/checkout</c>.
    /// </summary>
    public string? StripePriceId { get; set; }

    /// <summary>
    /// Whether this plan is offered for self-service online payment via PayHere. Surfaced on
    /// <c>GET /my-subscription/available-plans</c> (alongside Stripe-purchasable plans, gated by
    /// <see cref="StripePriceId"/>) once true. Defaults to <c>false</c>.
    /// </summary>
    public bool IsOnline { get; set; }
}
