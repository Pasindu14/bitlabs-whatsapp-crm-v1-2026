using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Messages.Entities;

public enum MessageDirection { Outbound, Inbound }

// Forward-only delivery lifecycle (Sent < Delivered < Read; Failed is terminal). Stored as text,
// so appending values never needs a backfill. Status webhooks (6.4) advance via StatusRank.
public enum MessageStatus { Sent, Failed, Delivered, Read }

/// <summary>
/// A single WhatsApp message sent by the tenant to a contact.
/// Tenant-scoped via CompanyId; isolated by the global query filter.
/// </summary>
public class Message : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid ContactId { get; set; }
    public Guid WabaConnectionId { get; set; }

    /// <summary>Owning conversation. Nullable: messages sent before the Conversation module (and any
    /// row created outside a thread) have none. Inbound + conversation-scoped sends always set it.</summary>
    public Guid? ConversationId { get; set; }

    public string Body { get; set; } = string.Empty;

    public MessageDirection Direction { get; set; } = MessageDirection.Outbound;
    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    /// <summary>Message ID returned by Meta's API (wamid). Null when send failed.</summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>Error message from Meta when Status == Failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Meta error code when Status == Failed (from statuses[].errors[0].code).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>UTC of the latest status transition (from a delivery-status webhook). Null until first update.</summary>
    public DateTime? StatusAt { get; set; }

    /// <summary>Whether Meta billed this message (from statuses[].pricing.billable). Null until known.</summary>
    public bool? Billable { get; set; }

    /// <summary>Pricing category from Meta (marketing/utility/authentication/service).</summary>
    public string? Category { get; set; }

    /// <summary>FK to Campaign when this message was sent via a campaign. Null for direct sends.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>
    /// Which run of the owning campaign produced this message (see <c>Campaign.CurrentRunNumber</c>). Lets a
    /// re-firing Recurring campaign re-send to the same contacts without the batch's per-run duplicate guard
    /// mistaking a prior run's message for this one. Null for direct sends; existing campaign rows are
    /// backfilled to 1 by migration.
    /// </summary>
    public int? CampaignRunNumber { get; set; }

    /// <summary>
    /// The subscription whose <c>MessagesUsedThisPeriod</c> this send incremented. Recorded at meter time so
    /// a later failure refunds the EXACT subscription that was charged — not merely "the current active one"
    /// (which may be a different row after a renewal / re-subscribe, causing a cross-period free-quota bug).
    /// Null for sends that weren't metered (no active subscription) or messages predating this column.
    /// </summary>
    public Guid? MeteredSubscriptionId { get; set; }

    public Contact Contact { get; set; } = null!;
    public WabaConnection WabaConnection { get; set; } = null!;
    public Conversations.Entities.Conversation? Conversation { get; set; }
}
