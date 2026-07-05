using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace wa_api.Features.Conversations.Realtime;

/// <summary>
/// Real-time inbox channel. On connect the client is auto-joined to its company's group, derived from the
/// authenticated <c>companyId</c> claim SERVER-SIDE — never from client input — so a tenant can only ever
/// receive its own company's chat events. The server only pushes the "message" event; clients invoke nothing.
/// <para>Browsers can't set an Authorization header on the WebSocket handshake, so the JWT arrives via the
/// <c>?access_token=</c> query string (wired in AuthenticationExtensions).</para>
/// </summary>
// CompanyAdmin AND Agent — matches the ConversationsController plane so agents get realtime inbox events for
// the threads they can already load over REST (L10). The company group is derived from the server-side
// companyId claim, so a tenant only ever receives its own company's events regardless of role.
[Authorize(Roles = "CompanyAdmin,Agent")]
public sealed class ChatHub : Hub
{
    /// <summary>SignalR group name for a company.</summary>
    public static string GroupFor(Guid companyId) => $"company:{companyId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirstValue("companyId"), out var companyId))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(companyId));

        await base.OnConnectedAsync();
    }
}
