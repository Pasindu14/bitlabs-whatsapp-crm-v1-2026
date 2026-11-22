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
/// Whether the auto-reply bot is answering this thread. Stored as text, like every other enum here.
/// </summary>
public enum ConversationBotState
{
    /// <summary>The bot answers inbound messages on this thread.</summary>
    Active,

    /// <summary>A human is handling it; the bot stays out of the way.</summary>
    Paused,
}

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

    /// <summary>
    /// Whether the bot may answer here. Paused when an agent sends manually, or when the AI itself
    /// decides the thread needs a person.
    /// <para>
    /// This lives on the conversation rather than the contact (decision D1): the inbox and the reply
    /// job both already load the conversation, so the guard costs no extra join, and an auto-resume
    /// timer is naturally thread-scoped.
    /// </para>
    /// </summary>
    public ConversationBotState BotState { get; set; } = ConversationBotState.Active;

    /// <summary>UTC time the bot was paused. Null while active.</summary>
    public DateTime? BotPausedAt { get; set; }

    /// <summary>Agent who took over, when a person did. Null when the bot paused itself.</summary>
    public Guid? BotPausedByUserId { get; set; }

    /// <summary>
    /// UTC time the bot picks the thread back up on its own. Null means manual hand-back only, which
    /// is what an explicit handoff sets — a thread escalated to a person must not un-escalate itself.
    /// </summary>
    public DateTime? BotResumesAt { get; set; }

    /// <summary>
    /// How many times the assistant has brought up booking in this thread, ever.
    /// <para>
    /// The two-attempt limit used to be enforced by the model counting its own asks out of the
    /// transcript, which works until the transcript is trimmed — past twenty messages the earlier
    /// asks fall out of context and it starts pushing again, having forgotten it already tried. This
    /// column is the authoritative count and survives trimming.
    /// </para>
    /// <para>
    /// Counts the ASSISTANT raising it. A customer asking for a time themselves is always helped,
    /// whatever this says.
    /// </para>
    /// </summary>
    public int BotAsksMade { get; set; }

    /// <summary>
    /// When the assistant last booked someone in on this thread. Set, it stops selling — there is
    /// nothing more transactional than being sold an appointment you have already made.
    /// Also resets <see cref="BotAsksMade"/>, so a later conversation starts fresh.
    /// </summary>
    public DateTime? BotConvertedAt { get; set; }

    /// <summary>
    /// When the assistant last sent an unprompted nudge after the customer went quiet.
    /// <para>
    /// Cleared on every inbound message, so each new silence can earn exactly one follow-up and a long
    /// thread cannot accumulate them. Null means this silence has not been chased yet.
    /// </para>
    /// </summary>
    public DateTime? BotFollowUpSentAt { get; set; }

    /// <summary>
    /// How many steps of the follow-up sequence this silence has already had. Cleared with
    /// <see cref="BotFollowUpSentAt"/> on every inbound message.
    /// <para>
    /// Each scheduled job names the step it is for and stands down unless this equals that step — the
    /// same stamp-before-send idempotency the single nudge had, extended to a sequence.
    /// </para>
    /// </summary>
    public int BotFollowUpStepsSent { get; set; }

    public Contact Contact { get; set; } = null!;
    public WabaConnection WabaConnection { get; set; } = null!;
}
