using Microsoft.AspNetCore.SignalR;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Messages.Entities;

namespace wa_api.Features.Conversations.Realtime;

/// <summary>
/// <see cref="IChatNotifier"/> over SignalR. <see cref="IHubContext{T}"/> is a singleton and safe to use
/// from any scope — including the webhook background job — so the same push works for inbound and outbound.
/// </summary>
public sealed class ChatNotifier(IHubContext<ChatHub> hub) : IChatNotifier
{
    public Task MessageAsync(
        Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
        CancellationToken ct = default)
        => hub.Clients
            .Group(ChatHub.GroupFor(companyId))
            .SendAsync("message", new { conversation, message }, ct);

    public Task MessageDeletedAsync(
        Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
        CancellationToken ct = default)
        => hub.Clients
            .Group(ChatHub.GroupFor(companyId))
            .SendAsync("messageDeleted", new { conversationId, messageId, conversation }, ct);

    public Task BotStateChangedAsync(
        Guid companyId, ConversationResponse conversation, CancellationToken ct = default)
        => hub.Clients
            .Group(ChatHub.GroupFor(companyId))
            .SendAsync("botStateChanged", new { conversation }, ct);

    public Task BotTypingAsync(
        Guid companyId, Guid conversationId, bool isTyping, CancellationToken ct = default)
        => hub.Clients
            .Group(ChatHub.GroupFor(companyId))
            .SendAsync("botTyping", new { conversationId, isTyping }, ct);

    public Task MessageStatusAsync(
        Guid companyId, Guid conversationId, Guid messageId, MessageStatus status, DateTime? statusAt,
        string? errorMessage, CancellationToken ct = default)
        => hub.Clients
            .Group(ChatHub.GroupFor(companyId))
            .SendAsync("messageStatus", new { conversationId, messageId, status = status.ToString(), statusAt, errorMessage }, ct);
}
