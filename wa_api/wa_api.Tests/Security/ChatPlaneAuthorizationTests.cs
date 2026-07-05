using Microsoft.AspNetCore.Authorization;
using wa_api.Features.Conversations.Controllers;
using wa_api.Features.Conversations.Realtime;
using Xunit;

namespace wa_api.Tests.Security;

/// <summary>
/// Pins the chat-plane role model (L10): the inbox REST controller and the realtime hub must both admit
/// CompanyAdmin AND Agent, and stay in lock-step — if one is widened/narrowed without the other, agents
/// would get realtime events they can't load over REST (or vice-versa).
/// </summary>
public class ChatPlaneAuthorizationTests
{
    private static string? RolesOf(Type t) => t
        .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
        .Cast<AuthorizeAttribute>()
        .Select(a => a.Roles)
        .FirstOrDefault(r => !string.IsNullOrEmpty(r));

    [Fact]
    public void ConversationsController_AllowsCompanyAdminAndAgent()
    {
        var roles = RolesOf(typeof(ConversationsController));
        Assert.NotNull(roles);
        Assert.Contains("CompanyAdmin", roles!);
        Assert.Contains("Agent", roles!);
    }

    [Fact]
    public void ChatHub_MatchesTheConversationsControllerRoles()
    {
        Assert.Equal(RolesOf(typeof(ConversationsController)), RolesOf(typeof(ChatHub)));
    }
}
