using wa_api.Common.Entities;

namespace wa_api.Features.Packages.Entities;

/// <summary>
/// An add-on bundle of extra message credits in the shared platform catalog. PLATFORM-LEVEL:
/// like <see cref="Plans.Entities.Plan"/> this is NOT tenant-scoped — it carries no
/// <c>CompanyId</c> and is not covered by the global query filter. Managed ONLY by SuperAdmin.
/// <para>
/// Adding a package to a company tops up its subscription's
/// <see cref="Subscriptions.Entities.Subscription.ExtraMessageCredits"/> by <see cref="ExtraMessages"/>.
/// Credits stack on top of the plan's monthly quota and persist across period resets (they are
/// a one-time purchase, not a recurring grant). Stripe is intentionally absent in this slice:
/// <see cref="Price"/> + <see cref="Currency"/> are display-only (company-admin self-service via a
/// payment gateway is a later phase; for now only SuperAdmin assigns packages).
/// </para>
/// <para>Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>.</para>
/// </summary>
public class MessagePackage : BaseEntity
{
    /// <summary>Display name. Unique platform-wide (e.g. "10k Top-up", "Bulk Pack").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional human description shown in the catalog.</summary>
    public string? Description { get; set; }

    /// <summary>Number of extra message credits this package grants when added to a subscription.</summary>
    public int ExtraMessages { get; set; }

    /// <summary>Display price for the bundle. Not charged in this slice (no Stripe yet).</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code for <see cref="Price"/>.</summary>
    public string Currency { get; set; } = "USD";
}
