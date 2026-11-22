using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Conversations.Realtime;

/// <summary>
/// Pushes chat events to a company's SignalR group. Abstracts <c>IHubContext</c> so the service and the
/// webhook handler don't bind to SignalR types directly (and stay unit-testable). A single "message"
/// event carries both the updated conversation snapshot (for the inbox list to reorder / re-badge) and
/// the new message (for an open thread to append) — emitted on every write, inbound or outbound.
/// </summary>
public interface IChatNotifier
{
    Task MessageAsync(
        Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
        CancellationToken ct = default);

    /// <summary>
    /// Pushes a "messageDeleted" event so every open inbox/thread for the company drops the message
    /// (and refreshes the list preview) without a manual refresh. Carries the updated conversation
    /// snapshot (its preview may have changed when the latest message was the one deleted).
    /// </summary>
    Task MessageDeletedAsync(
        Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
        CancellationToken ct = default);

    /// <summary>
    /// Pushes a "botStateChanged" event so a second agent's open inbox sees a takeover or a hand-back
    /// live, rather than starting to type over the colleague who just stepped in.
    /// </summary>
    Task BotStateChangedAsync(
        Guid companyId, ConversationResponse conversation, CancellationToken ct = default);

    /// <summary>
    /// Pushes a "botTyping" event while the assistant is working on a reply, so the inbox shows that
    /// something is happening instead of dead air. A reply takes several seconds — the model call plus
    /// the deliberate read delay — which otherwise looks like nothing happened.
    /// <para>
    /// Advisory and best-effort: a job that dies never sends the closing <c>false</c>, so the client
    /// must also clear the indicator on a timeout and when the next message arrives, rather than
    /// trusting this to always be balanced.
    /// </para>
    /// </summary>
    Task BotTypingAsync(
        Guid companyId, Guid conversationId, bool isTyping, CancellationToken ct = default);

    /// <summary>
    /// Pushes a "messageStatus" event when Meta reports an outbound message sent/delivered/read/failed, so
    /// an open thread's ticks move live instead of only on the next refetch.
    /// </summary>
    Task MessageStatusAsync(
        Guid companyId, Guid conversationId, Guid messageId, MessageStatus status, DateTime? statusAt,
        string? errorMessage, CancellationToken ct = default);
}
