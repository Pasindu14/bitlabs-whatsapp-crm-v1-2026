using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>
/// A single message inside a conversation thread. <see cref="StatusAt"/> carries the latest delivery
/// transition (from status webhooks) for outbound messages. All timestamps are UTC.
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
    DateTime? StatusAt
);
