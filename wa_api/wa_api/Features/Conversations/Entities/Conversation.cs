using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Conversations.Entities;

/// <summary>
/// Lifecycle of a conversation. v1 only ever creates <see cref="Open"/>; the partial-unique index
/// keys off it so a future "resolve/close" can reuse the pattern without a data backfill. Stored as
/// text (like every other enum here), so appending values never needs a migration of existing rows.
/// </summary>
public enum ConversationStatus { Open, Closed }

/// <summary>
/// A WhatsApp thread between a tenant <see cref="Contact"/> and the company over one WABA number.
/// Tenant-scoped via <see cref="CompanyId"/> and isolated by the global query filter, exactly like
/// <see cref="Message"/>. The snapshot fields (<see cref="LastMessageAt"/>, <see cref="LastMessageBody"/>,
/// <see cref="LastMessageDirection"/>, <see cref="UnreadCount"/>, <see cref="WindowExpiresAt"/>)
/// denormalize the latest message so the inbox list sorts and renders without a per-row join.
/// <para>All <see cref="DateTime"/> values are UTC.</para>
/// </summary>
public class Conversation : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Set from tenant context on the HTTP path, or explicitly on the
    /// webhook path (background jobs have no tenant context — never relies on auto-stamping there).</summary>
    public Guid CompanyId { get; set; }

    public Guid ContactId { get; set; }
    public Guid WabaConnectionId { get; set; }

    public ConversationStatus Status { get; set; } = ConversationStatus.Open;

    /// <summary>UTC timestamp of the most recent message (inbound or outbound) — the inbox sort key.</summary>
    public DateTime LastMessageAt { get; set; }

    /// <summary>Truncated preview of the most recent message body (≤200 chars).</summary>
    public string? LastMessageBody { get; set; }

    /// <summary>Direction of the most recent message.</summary>
    public MessageDirection LastMessageDirection { get; set; }

    /// <summary>Inbound messages not yet read in the shared inbox. Incremented on inbound, reset to 0
    /// when an agent opens the thread (POST /conversations/{id}/read). Shared-inbox count, not per-user.</summary>
    public int UnreadCount { get; set; }

    /// <summary>UTC end of Meta's 24-hour customer-service window; reset to now+24h on each inbound
    /// message. Null for a business-initiated thread the customer hasn't replied to yet. Free-form
    /// (non-template) sends are only permitted while this is in the future.</summary>
    public DateTime? WindowExpiresAt { get; set; }

    public Contact Contact { get; set; } = null!;
    public WabaConnection WabaConnection { get; set; } = null!;
}
