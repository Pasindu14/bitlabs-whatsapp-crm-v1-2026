using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using wa_api.Common.Errors;
using wa_api.Features.Auth;

namespace wa_api.Common.Authorization;

/// <summary>
/// Requires the caller to hold the named permission constant (see <see cref="Permission"/>).
/// SuperAdmin and CompanyAdmin are unconditionally allowed (all-access by role).
/// For Agent callers the JWT <c>permissions</c> claim array is checked.
/// Always compose with <c>[Authorize]</c> — this filter runs after role gating.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequirePermissionAttribute(string permission) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var role = user.FindFirstValue("role");

        if (role is nameof(UserRole.SuperAdmin) or nameof(UserRole.CompanyAdmin))
        {
            await next();
            return;
        }

        var hasPermission = user.Claims.Any(c => c.Type == "permissions" && c.Value == permission);
        if (!hasPermission)
            throw new PermissionDeniedException(permission);

        await next();
    }
}
