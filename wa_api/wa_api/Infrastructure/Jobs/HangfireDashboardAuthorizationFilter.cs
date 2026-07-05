using Hangfire.Dashboard;

namespace wa_api.Infrastructure.Jobs;

/// <summary>
/// Gates the Hangfire dashboard. Always allowed in Development; in every other environment it requires an
/// authenticated <c>SuperAdmin</c> (L4). The dashboard route is not behind <c>[Authorize]</c>, so a plain
/// browser request is anonymous and correctly denied (fail-closed) — but a request carrying a valid
/// SuperAdmin token is allowed, giving prod job visibility without loosening the gate to "anyone".
/// </summary>
public class HangfireDashboardAuthorizationFilter(IWebHostEnvironment env) : IDashboardAuthorizationFilter
{
    private readonly IWebHostEnvironment _env = env;

    public bool Authorize(DashboardContext context)
    {
        if (_env.IsDevelopment())
            return true;

        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole("SuperAdmin");
    }
}
