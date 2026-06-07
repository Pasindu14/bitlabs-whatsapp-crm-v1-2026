using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;

namespace wa_api.Features.Subscriptions.Controllers;

/// <summary>
/// Tenant-facing read of the caller's OWN subscription + quota usage. Any authenticated
/// company user may read it; <c>CompanyId</c> comes from the JWT (never the client) and the
/// global query filter enforces isolation.
/// </summary>
[ApiController]
[Route("my-subscription")]
[Authorize]
public class MySubscriptionController(ISubscriptionService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/my-subscription — the caller's active plan + usage (empty shape if none).</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var result = await service.GetMineAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
