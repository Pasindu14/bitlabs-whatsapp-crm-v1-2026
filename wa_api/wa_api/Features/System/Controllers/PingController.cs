using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;

namespace wa_api.Features.System.Controllers;

/// <summary>
/// Phase 0.2 smoke-test endpoint: proves the standard <see cref="ApiResponse{T}"/>
/// envelope, the <c>/api/v1</c> prefix, camelCase serialization, and the
/// exception → <see cref="ApiError"/> mapping are all wired correctly.
/// </summary>
[ApiController]
[Route("ping")]
public class PingController : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/ping → success envelope.</summary>
    [HttpGet]
    public IActionResult Get()
        => Ok(ResponseHelper.Ok(new { status = "ok", utcTime = DateTime.UtcNow }, CorrelationId));

    /// <summary>GET /api/v1/ping/boom → demonstrates a mapped 404 ApiError.</summary>
    [HttpGet("boom")]
    public IActionResult Boom()
        => throw new NotFoundException("Sample", "does-not-exist");
}
