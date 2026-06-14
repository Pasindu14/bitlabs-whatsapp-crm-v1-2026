using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Messages.Dtos;

namespace wa_api.Features.Conversations;

public interface IConversationService
{
    /// <summary>Paged inbox for the caller's company, newest <c>LastMessageAt</c> first, searchable by
    /// contact name/phone.</summary>
    Task<(IReadOnlyList<ConversationResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, CancellationToken ct = default);

    /// <summary>Single conversation; throws 404 when it isn't visible to the caller.</summary>
    Task<ConversationResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Paged message history for a conversation, newest-first (page 1 = latest).</summary>
    Task<(IReadOnlyList<ConversationMessageResponse> Items, int Total)> GetMessagesAsync(
        Guid conversationId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Reply within a conversation. Pre-checks the 24-hour window (WINDOW_CLOSED), sends through
    /// the shared pathway, updates the snapshot, and pushes a realtime event. Throws on send failure.</summary>
    Task<ConversationMessageResponse> SendMessageAsync(Guid conversationId, string body, CancellationToken ct = default);

    /// <summary>Resets a conversation's unread count to zero (agent opened the thread).</summary>
    Task MarkReadAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Standalone-endpoint adapter: send to a contact, auto-threading into their open
    /// conversation so the message appears in the inbox. Returns the legacy <see cref="MessageResponse"/>.</summary>
    Task<MessageResponse> SendToContactAsync(
        Guid contactId, Guid wabaConnectionId, string body, CancellationToken ct = default);

    /// <summary>Starts (or reuses) a contact's open conversation and sends the first message, returning the
    /// conversation so the client can open the thread. A closed-window send failure is recorded on the
    /// message (and visible in the thread), not thrown.</summary>
    Task<ConversationResponse> StartConversationAsync(
        Guid contactId, Guid wabaConnectionId, string body, CancellationToken ct = default);
}
