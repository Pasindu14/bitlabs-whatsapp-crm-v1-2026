using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;
using wa_api.Features.Subscriptions.Entities;

namespace wa_api.Features.Packages.Entities;

/// <summary>
/// An audit record of a <see cref="MessagePackage"/> being added to a company's
/// <see cref="Subscription"/>. Tenant-scoped (one row per add) so the company's package history
/// is queryable and survives catalog edits. Field values are SNAPSHOTTED at purchase time
/// (<see cref="PackageName"/>, <see cref="MessagesAdded"/>, <see cref="Price"/>) so later edits to
/// the catalog package don't rewrite history. This table is also where the future
/// company-admin/payment-gateway path will write its purchases.
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive from <see cref="BaseEntity"/>;
/// is tenant-scoped via <see cref="ITenantEntity.CompanyId"/>.
/// </para>
/// </summary>
public class PackagePurchase : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The subscription the credits were applied to.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>The catalog package that was added. Restrict-deleted so history keeps its FK.</summary>
    public Guid PackageId { get; set; }

    /// <summary>Package name at purchase time (snapshot).</summary>
    public string PackageName { get; set; } = null!;

    /// <summary>Credits granted by this purchase (snapshot of <see cref="MessagePackage.ExtraMessages"/>).</summary>
    public int MessagesAdded { get; set; }

    /// <summary>Price at purchase time (snapshot).</summary>
    public decimal Price { get; set; }

    /// <summary>Currency at purchase time (snapshot).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Owning company navigation.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>Applied-to subscription navigation.</summary>
    public Subscription Subscription { get; set; } = null!;

    /// <summary>Purchased catalog package navigation.</summary>
    public MessagePackage Package { get; set; } = null!;
}
