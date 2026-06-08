using Hangfire.Dashboard;

namespace wa_api.Infrastructure.Jobs;

/// <summary>
/// Phase 0.4: the Hangfire dashboard is reachable only in Development.
/// Real super-admin authorization is wired in Phase 2 (per PRD 0.4).
/// </summary>
public class HangfireDashboardAuthorizationFilter(IWebHostEnvironment env) : IDashboardAuthorizationFilter
{
    private readonly IWebHostEnvironment _env = env;

    public bool Authorize(DashboardContext context) => _env.IsDevelopment();
}
