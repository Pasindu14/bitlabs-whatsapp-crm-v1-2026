using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Notifications.Entities;

public enum NotificationType
{
    QuotaWarning3Days,
    QuotaWarning2Days,
    QuotaWarning1Day,
    QuotaWarningToday,
    CampaignFailed,
    TemplateApproved,
}

/// <summary>
/// Persisted in-app notification scoped to a company. Quota warnings are idempotent
/// per campaign per type (unique index on CampaignId + Type).
/// </summary>
public class Notification : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Null for non-campaign notifications; set for quota warnings and failure alerts.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>Null for non-template notifications; set for template approval alerts.</summary>
    public Guid? TemplateId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public Campaign? Campaign { get; set; }

    public Template? Template { get; set; }
}
