using Microsoft.AspNetCore.Authorization;
using wa_api.Features.Users.Controllers;
using Xunit;

namespace wa_api.Tests.Security;

/// <summary>
/// Guard for L6: the cross-tenant <see cref="UsersController"/> reaches <c>UserService</c> methods that run
/// WITHOUT a global query filter and trust the body's CompanyId. That is only safe because the controller is
/// gated to SuperAdmin. This test fails loudly if that gate is ever removed or widened, so the unscoped path
/// can never quietly become reachable by a tenant role.
/// </summary>
public class UsersControllerAuthorizationTests
{
    [Fact]
    public void UsersController_IsGatedToSuperAdminOnly()
    {
        var authorize = typeof(UsersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        Assert.NotEmpty(authorize);
        Assert.Contains(authorize, a => a.Roles == "SuperAdmin");
        // No [AllowAnonymous] escape hatch on the controller.
        Assert.Empty(typeof(UsersController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }
}
