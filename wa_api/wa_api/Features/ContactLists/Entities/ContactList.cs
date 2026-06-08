using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;

namespace wa_api.Features.ContactLists.Entities;

/// <summary>
/// A named grouping of contacts owned by a tenant <see cref="Company"/> (e.g. "VIP",
/// "Newsletter"). A contact may belong to zero, one, or many lists via
/// <see cref="ContactListMember"/>. Tenant-scoped + soft-deletable (from <see cref="BaseEntity"/>).
/// </summary>
public class ContactList : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Auto-stamped from tenant context; never from the client.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>List name. Unique per company.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }
}
