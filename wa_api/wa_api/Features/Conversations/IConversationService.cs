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

    /// <summary>
    /// Sends an image, audio file, video or document inside the open 24-hour window. Validates type and size,
    /// uploads to Meta, sends by media id, keeps a copy on disk for the thread, and pushes a realtime event.
    /// Throws <c>MEDIA_*</c> / <c>WINDOW_CLOSED</c> before anything is sent, <c>WHATSAPP_SEND_FAILED</c> if Meta
    /// refuses the message afterwards.
    /// </summary>
    Task<ConversationMessageResponse> SendMediaAsync(
        Guid conversationId, byte[] data, string fileName, string contentType, string? caption,
        CancellationToken ct = default);

    /// <summary>Resets a conversation's unread count to zero (agent opened the thread).</summary>
    Task MarkReadAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// "Delete for me": hides a message from the tenant's inbox + history (soft delete). This is a
    /// CRM-side action only — WhatsApp's Cloud API can't revoke a delivered message, so the customer's
    /// copy is untouched. Idempotent (already-deleted is a no-op). Recomputes the conversation preview
    /// when the deleted message was the latest, then pushes a realtime "messageDeleted" event.
    /// Throws 404 when the message isn't in the conversation (or isn't visible to the caller).
    /// </summary>
    Task DeleteMessageForMeAsync(Guid conversationId, Guid messageId, CancellationToken ct = default);

    /// <summary>Standalone-endpoint adapter: send to a contact, auto-threading into their open
    /// conversation so the message appears in the inbox. Returns the legacy <see cref="MessageResponse"/>.</summary>
    Task<MessageResponse> SendToContactAsync(
        Guid contactId, Guid wabaConnectionId, string body, CancellationToken ct = default);

    /// <summary>Starts (or reuses) a contact's open conversation and sends the first message, returning the
    /// conversation so the client can open the thread. Sends <paramref name="templateId"/> (an approved,
    /// body-only template filled with <paramref name="templateVariables"/>) when given, otherwise
    /// <paramref name="body"/> as free text — which WhatsApp only accepts inside the 24-hour window, so free
    /// text to a contact outside it is refused with <c>WINDOW_CLOSED</c> before anything is sent.</summary>
    Task<ConversationResponse> StartConversationAsync(
        Guid contactId, Guid wabaConnectionId, string? body,
        Guid? templateId = null, IReadOnlyList<string>? templateVariables = null, CancellationToken ct = default);

    /// <summary>
    /// Takes the thread away from the bot, or hands it back. Pausing with no duration means the agent
    /// owns it until they say otherwise; a duration sets an auto-resume that <c>BotAutoResumeJob</c>
    /// picks up. Note that replying by hand pauses the bot on its own — this is the explicit control.
    /// </summary>
    Task<ConversationResponse> SetBotStateAsync(
        Guid conversationId, bool paused, int? resumeAfterMinutes, CancellationToken ct = default);
}
