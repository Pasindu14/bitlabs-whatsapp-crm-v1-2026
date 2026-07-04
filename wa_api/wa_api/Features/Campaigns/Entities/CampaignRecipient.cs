using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Campaigns.Entities;

public enum RecipientStatus
{
    Queued,
    /// <summary>Transient: a batch has atomically claimed this recipient (Queued→Sending) and is calling
    /// Meta. Only the batch that wins the claim sends, so two concurrent batches can never double-send the
    /// same recipient. Reverted to Queued on a retriable failure; advanced to Sent/Failed on completion; a
    /// stale Sending row (crashed mid-send) is reclaimed to Queued by the next batch. Stored as text, so
    /// inserting it here needs no migration.</summary>
    Sending,
    Sent,
    Delivered,
    Read,
    Failed,
    Skipped
}

/// <summary>
/// One row per contact in a campaign. Created in bulk by CampaignLaunchJob when a campaign fires.
/// Status advances as Meta delivery webhooks arrive (via MessageStatusWebhookHandler).
/// </summary>
public class CampaignRecipient : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ContactId { get; set; }

    /// <summary>FK to Message — set after the message is successfully sent to Meta.</summary>
    public Guid? MessageId { get; set; }

    public RecipientStatus Status { get; set; } = RecipientStatus.Queued;

    /// <summary>Meta error code when Status == Failed.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// True when this message was sent despite the contact having no recorded opt-in — i.e. the
    /// campaign's consent override was on at send time. Audit trail for compliance / quality monitoring.
    /// </summary>
    public bool SentWithoutConsent { get; set; } = false;

    /// <summary>
    /// Snapshot of resolved variable values at send time for audit trail.
    /// e.g. { "body_1": "John", "body_2": "Acme Corp" }
    /// </summary>
    public string ResolvedVariables { get; set; } = "{}";

    /// <summary>
    /// Idempotency key preventing double-sends on job retry.
    /// Format: campaign:{campaignId}:contact:{contactId}
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public Campaign Campaign { get; set; } = null!;
    public Contact Contact { get; set; } = null!;
    public Message? Message { get; set; }
}
