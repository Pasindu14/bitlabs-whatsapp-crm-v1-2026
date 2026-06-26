using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;

namespace wa_api.Features.AdminReports;

/// <summary>
/// SuperAdmin-only reporting endpoints for the admin panel. Read-only aggregations across
/// all tenants: packages purchased by date range, company balances, platform KPIs, usage.
/// </summary>
[ApiController]
[Route("admin-reports")]
[Authorize(Roles = "SuperAdmin")]
public class AdminReportsController(AdminReportsService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/admin-reports/packages?from=&amp;to= — packages purchased in a date range.</summary>
    [HttpGet("packages")]
    public async Task<IActionResult> GetPackages(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await service.GetPackagesAsync(from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/admin-reports/balances — remaining message quota per company.</summary>
    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances(CancellationToken ct)
    {
        var result = await service.GetBalancesAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/admin-reports/dashboard — headline platform KPIs.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var result = await service.GetDashboardAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/admin-reports/usage?from=&amp;to= — per-company message volume.</summary>
    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var result = await service.GetUsageAsync(from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
