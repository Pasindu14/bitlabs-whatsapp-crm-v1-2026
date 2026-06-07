using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.ContactLists.Entities;

/// <summary>
/// Join row linking a <c>Contact</c> to a <see cref="ContactList"/>. Removing a contact from a
/// list hard-deletes this row only — the contact itself is untouched and may stay in other lists
/// (or none). Tenant-scoped (CompanyId auto-stamped) so the global filter isolates it per company.
/// </summary>
public class ContactListMember : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Auto-stamped from tenant context.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The list this membership belongs to.</summary>
    public Guid ContactListId { get; set; }

    /// <summary>The contact that is a member of the list.</summary>
    public Guid ContactId { get; set; }
}
