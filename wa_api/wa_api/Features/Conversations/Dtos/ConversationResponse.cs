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
    DateTime CreatedAt,
    /// <summary>Whether the bot is answering this thread, so the composer can say who has it.</summary>
    ConversationBotState BotState,
    /// <summary>When the bot takes the thread back on its own. Null means manual hand-back only.</summary>
    DateTime? BotResumesAt,
    /// <summary>
    /// True when a person paused the bot here, false when the bot handed the thread over itself (or it is
    /// not paused). Lets the inbox say "the bot passed this to the team" instead of "you're handling it".
    /// </summary>
    bool BotPausedByAgent = false
);
