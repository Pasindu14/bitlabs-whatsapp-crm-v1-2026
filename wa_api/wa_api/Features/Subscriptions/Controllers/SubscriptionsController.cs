using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Subscriptions.Dtos;

namespace wa_api.Features.Subscriptions.Controllers;

/// <summary>
/// Subscription administration. PLATFORM-LEVEL: every endpoint is restricted to <c>SuperAdmin</c>.
/// Companies are placed on plans here (manual assignment — no Stripe in this slice).
/// </summary>
[ApiController]
[Route("subscriptions")]
[Authorize(Roles = "SuperAdmin")]
public class SubscriptionsController(ISubscriptionService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/subscriptions — paged list across all companies.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        var (items, total) = await service.GetPagedAsync(page, pageSize, search, sortBy, sortOrder, ct);
        return Ok(ResponseHelper.Paged(items, page, pageSize, total, CorrelationId));
    }

    /// <summary>GET /api/v1/subscriptions/company/{companyId} — a company's active subscription.</summary>
    [HttpGet("company/{companyId:guid}")]
    public async Task<IActionResult> GetForCompany(Guid companyId, CancellationToken ct)
    {
        var result = await service.GetForCompanyAsync(companyId, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/subscriptions/assign — place a company onto a plan.</summary>
    [HttpPost("assign")]
    public async Task<IActionResult> Assign(AssignSubscriptionRequest request, CancellationToken ct)
    {
        var result = await service.AssignAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/subscriptions/company/{companyId}/plan — change a company's plan.</summary>
    [HttpPut("company/{companyId:guid}/plan")]
    public async Task<IActionResult> ChangePlan(Guid companyId, ChangePlanRequest request, CancellationToken ct)
    {
        var result = await service.ChangePlanAsync(companyId, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/subscriptions/company/{companyId}/cancel — cancel a company's subscription.</summary>
    [HttpPost("company/{companyId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid companyId, CancellationToken ct)
    {
        var result = await service.CancelAsync(companyId, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/subscriptions/company/{companyId}/packages — add credit package to a company.</summary>
    [HttpPost("company/{companyId:guid}/packages")]
    public async Task<IActionResult> AddPackage(Guid companyId, AddPackageRequest request, CancellationToken ct)
    {
        var result = await service.AddPackageAsync(companyId, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/subscriptions/company/{companyId}/packages — a company's package-purchase history.</summary>
    [HttpGet("company/{companyId:guid}/packages")]
    public async Task<IActionResult> GetPackageHistory(Guid companyId, CancellationToken ct)
    {
        var result = await service.GetPackageHistoryAsync(companyId, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
