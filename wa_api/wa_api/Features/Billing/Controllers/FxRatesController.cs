using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Infrastructure.Fx;

namespace wa_api.Features.Billing.Controllers;

/// <summary>
/// Reference exchange rates for price display. Authenticated-only: both callers are signed in
/// (CompanyAdmin on the plan picker, SuperAdmin on the plans table), and leaving it anonymous would
/// turn the API into a free FX proxy against our upstream's rate limit.
/// </summary>
[ApiController]
[Route("fx")]
[Authorize]
public class FxRatesController(IFxRateService fx) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>
    /// GET /api/v1/fx/usd-aed — indicative USD→AED rate for showing a dirham equivalent beside a USD
    /// plan price. Display only; customers are always charged in USD.
    /// </summary>
    [HttpGet("usd-aed")]
    public async Task<IActionResult> GetUsdToAed(CancellationToken ct)
    {
        var rate = await fx.GetUsdToAedAsync(ct);
        return Ok(ResponseHelper.Ok(rate, CorrelationId));
    }
}
