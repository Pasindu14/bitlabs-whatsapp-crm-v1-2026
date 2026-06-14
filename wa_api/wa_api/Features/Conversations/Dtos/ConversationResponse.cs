using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>
/// Inbox list / thread-header projection of a <see cref="Conversation"/> with its denormalized snapshot.
/// All <see cref="DateTime"/> values are UTC; the client renders them in the viewer's local zone.
/// </summary>
public record ConversationResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    string ContactPhone,
    Guid WabaConnectionId,
    string DisplayPhoneNumber,
    ConversationStatus Status,
    DateTime LastMessageAt,
    string? LastMessageBody,
    MessageDirection LastMessageDirection,
    int UnreadCount,
    DateTime? WindowExpiresAt,
    DateTime CreatedAt
);
