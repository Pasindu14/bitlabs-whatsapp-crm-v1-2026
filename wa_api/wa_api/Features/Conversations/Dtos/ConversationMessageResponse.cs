using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>
/// A single message inside a conversation thread. <see cref="StatusAt"/> carries the latest delivery
/// transition (from status webhooks) for outbound messages. All timestamps are UTC.
/// <para>
/// Media fields describe an inbound media message: <see cref="MediaType"/> is null for plain text; when set,
/// the client renders the media by fetching <c>/api/media/{Id}</c>. <see cref="MediaReady"/> is false until
/// the bytes have been downloaded from Meta (the client shows a placeholder, then swaps in the image on the
/// realtime "media ready" event).
/// </para>
/// </summary>
public record ConversationMessageResponse(
    Guid Id,
    Guid? ConversationId,
    string Body,
    MessageDirection Direction,
    MessageStatus Status,
    string? ExternalMessageId,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StatusAt,
    string? MediaType = null,
    string? MediaMimeType = null,
    bool MediaReady = false
);
