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

    /// <summary>Legacy single-list FK — kept nullable; prefer CampaignContactLists for multi-list campaigns.</summary>
    public Guid? ContactListId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// JSONB mapping of template variable positions to contact attribute keys.
    /// e.g. { "body": { "1": "Name", "2": "attributes.company" } }
    /// </summary>
    public string VariableMapping { get; set; } = "{}";

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Immediate;

    /// <summary>
    /// When true, the campaign sends to contacts without recorded opt-in consent, bypassing the
    /// NO_CONSENT skip gate in <c>CampaignBatchSendJob</c>. Does NOT bypass the opt-out or
    /// not-on-WhatsApp gates. Compliance risk sits with the operator — intended only when consent
    /// exists outside this system (e.g. an imported signup list). Defaults to false (safe).
    /// </summary>
    public bool OverrideConsentGate { get; set; } = false;

    /// <summary>
    /// Audit counter: messages this campaign sent to contacts without recorded opt-in (override on).
    /// Mirrors the per-recipient <c>CampaignRecipient.SentWithoutConsent</c> flag for cheap display.
    /// </summary>
    public int NoConsentSentCount { get; set; } = 0;

    /// <summary>UTC fire time for OneTime campaigns.</summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>Cron expression for Recurring campaigns (e.g. "0 9 * * 1" = every Monday at 9am).</summary>
    public string? RecurrenceCron { get; set; }

    /// <summary>Snapshot of contact count taken at launch time.</summary>
    public int TotalRecipients { get; set; }

    /// <summary>
    /// Which send run is currently active (1-based). Incremented each time a Recurring campaign is re-fired
    /// by its cron after the previous run completed. Recipient rows are re-queued and each resulting Message
    /// is stamped with this number, so the batch's per-run duplicate guard only skips sends from THIS run —
    /// a message from run 1 must not stop run 2 from sending. OneTime/Immediate campaigns stay at run 1.
    /// </summary>
    public int CurrentRunNumber { get; set; } = 1;

    /// <summary>Running count of messages enqueued/sent by the batch job.</summary>
    public int SentCount { get; set; }

    /// <summary>UTC timestamp when the first batch job fired.</summary>
    public DateTime? LaunchedAt { get; set; }

    /// <summary>UTC timestamp when the last recipient row was processed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Hangfire job id stored so we can cancel or reschedule.</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>
    /// Number of consecutive stall-recoveries the scheduler's sweeper has performed WITHOUT the batch making
    /// progress (no drop in the Queued backlog since the previous recovery). A deterministic fault (bad
    /// template payload, NRE) makes the batch throw every attempt; Hangfire deletes it after its retry budget
    /// and the sweeper would otherwise re-enqueue it every minute forever. Once this exceeds the sweeper's
    /// threshold the campaign is handed to <c>CampaignPoisonHandlerJob</c> (marked Failed + tenant notified)
    /// instead of being re-enqueued. Reset to 0 whenever a recovery observes progress or a fresh run launches.
    /// </summary>
    public int RecoveryAttempts { get; set; }

    /// <summary>
    /// The Queued-recipient count observed at the previous stall-recovery. The sweeper compares against it to
    /// tell a poison batch (backlog unchanged) from one making genuine progress (backlog shrinking), so a
    /// campaign that merely hit a few transient infra restarts is never wrongly failed. Null until first recovery.
    /// </summary>
    public int? LastRecoveryQueuedCount { get; set; }

    public Template Template { get; set; } = null!;
    public ContactList? ContactList { get; set; }
    public ICollection<CampaignContactList> ContactLists { get; set; } = [];
    public ICollection<CampaignContact> IndividualContacts { get; set; } = [];
    public ICollection<CampaignRecipient> Recipients { get; set; } = [];
}
