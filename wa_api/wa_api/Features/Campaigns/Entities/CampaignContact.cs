using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;

namespace wa_api.Features.Campaigns.Entities;

public class CampaignContact : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ContactId { get; set; }

    public Campaign Campaign { get; set; } = null!;
    public Contact Contact { get; set; } = null!;
}
