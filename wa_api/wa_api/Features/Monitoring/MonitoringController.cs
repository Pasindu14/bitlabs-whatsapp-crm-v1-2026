using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;

namespace wa_api.Features.Monitoring;

[ApiController]
[Route("monitoring")]
[Authorize(Roles = "SuperAdmin")]
public class MonitoringController(MonitoringService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/monitoring/company-health — per-company health metrics across all tenants.</summary>
    [HttpGet("company-health")]
    public async Task<IActionResult> GetCompanyHealth(CancellationToken ct)
    {
        var result = await service.GetCompanyHealthAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
