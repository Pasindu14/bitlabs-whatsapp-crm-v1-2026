using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Features.Bot.Dtos;

namespace wa_api.Features.Bot.Controllers;

/// <summary>
/// Auto-reply rule management for a tenant. COMPANY-SCOPED: every endpoint runs as the caller's
/// company (CompanyId from the JWT) and needs the <c>manage_bot</c> permission — CompanyAdmin holds
/// it implicitly, an Agent only if it was granted.
/// </summary>
[ApiController]
[Route("auto-reply-rules")]
[Authorize]
[RequirePermission(Permission.ManageBot)]
[RequirePlanFeature(Permission.ManageBot)]
[RefreshBotGate]
public class AutoReplyRulesController(IAutoReplyRuleService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/auto-reply-rules — all rules, in evaluation order.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await service.GetAllAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/auto-reply-rules/{id} — single rule.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/auto-reply-rules — create a rule.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateAutoReplyRuleRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/auto-reply-rules/{id} — update a rule.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateAutoReplyRuleRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/auto-reply-rules/{id} — soft-delete a rule.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }
}
