using wa_api.Features.Conversations.Dtos;

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
}
