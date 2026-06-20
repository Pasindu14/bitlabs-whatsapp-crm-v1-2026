using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Features.Auth;

namespace wa_api.Features.Analytics;

[ApiController]
[Route("analytics")]
[Authorize]
[RequirePermission(Permission.Analytics)]
public class AnalyticsController(AnalyticsService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/analytics/messages — daily message delivery metrics.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessageMetrics(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await service.GetMessageMetricsAsync(from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/analytics/campaigns — per-campaign delivery performance.</summary>
    [HttpGet("campaigns")]
    public async Task<IActionResult> GetCampaignPerformance(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await service.GetCampaignPerformanceAsync(from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/analytics/costs — billable message counts by category.</summary>
    [HttpGet("costs")]
    public async Task<IActionResult> GetCostAnalytics(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var result = await service.GetCostAnalyticsAsync(from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
