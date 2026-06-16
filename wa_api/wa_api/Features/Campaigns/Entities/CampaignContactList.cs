using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.ContactLists.Entities;

namespace wa_api.Features.Campaigns.Entities;

public class CampaignContactList : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ContactListId { get; set; }

    public Campaign Campaign { get; set; } = null!;
    public ContactList ContactList { get; set; } = null!;
}
