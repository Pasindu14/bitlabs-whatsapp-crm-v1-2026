using System.Text.Json;
using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.ContactLists.Entities;
using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Campaigns.Entities;

public enum CampaignStatus
{
    Draft,
    Scheduled,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum ScheduleType
{
    Immediate,
    OneTime,
    Recurring
}

/// <summary>
/// A bulk messaging campaign that sends an approved template to all contacts in a list.
/// Tenant-scoped via CompanyId; isolated by the global query filter.
/// </summary>
public class Campaign : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid ContactListId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// JSONB mapping of template variable positions to contact attribute keys.
    /// e.g. { "body": { "1": "Name", "2": "attributes.company" } }
    /// </summary>
    public string VariableMapping { get; set; } = "{}";

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Immediate;

    /// <summary>UTC fire time for OneTime campaigns.</summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>Cron expression for Recurring campaigns (e.g. "0 9 * * 1" = every Monday at 9am).</summary>
    public string? RecurrenceCron { get; set; }

    /// <summary>Snapshot of contact count taken at launch time.</summary>
    public int TotalRecipients { get; set; }

    /// <summary>Running count of messages enqueued/sent by the batch job.</summary>
    public int SentCount { get; set; }

    /// <summary>UTC timestamp when the first batch job fired.</summary>
    public DateTime? LaunchedAt { get; set; }

    /// <summary>UTC timestamp when the last recipient row was processed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Hangfire job id stored so we can cancel or reschedule.</summary>
    public string? HangfireJobId { get; set; }

    public Template Template { get; set; } = null!;
    public ContactList ContactList { get; set; } = null!;
    public ICollection<CampaignRecipient> Recipients { get; set; } = [];
}
