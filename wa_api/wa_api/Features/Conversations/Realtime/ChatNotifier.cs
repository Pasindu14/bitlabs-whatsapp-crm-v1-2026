using Microsoft.AspNetCore.SignalR;
using wa_api.Features.Conversations.Dtos;

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
}
