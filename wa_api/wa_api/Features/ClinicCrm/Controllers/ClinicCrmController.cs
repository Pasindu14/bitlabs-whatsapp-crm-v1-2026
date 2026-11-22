using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Bot;
using wa_api.Features.Auth;
using wa_api.Features.ClinicCrm.Dtos;

namespace wa_api.Features.ClinicCrm.Controllers;

/// <summary>
/// The company's link to its clinic's CRM (plan 014). COMPANY-SCOPED and gated on <c>manage_bot</c>,
/// like the rest of the assistant's settings.
/// </summary>
[ApiController]
[Route("clinic-crm")]
[Authorize]
[RequirePermission(Permission.ManageBot)]
[RequirePlanFeature(Permission.ManageBot)]
[RefreshBotGate]
public class ClinicCrmController(IClinicCrmSettingsService settings) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/clinic-crm — connection details (never the secret) and the service mapping.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await settings.GetAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/clinic-crm — create or update the connection.</summary>
    [HttpPut]
    public async Task<IActionResult> Save(SaveClinicCrmConnectionRequest request, CancellationToken ct)
    {
        var result = await settings.SaveConnectionAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/clinic-crm — disconnect; the bot books on our own calendar again.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await settings.DeleteConnectionAsync(ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }

    /// <summary>POST /api/v1/clinic-crm/test — token + treatment list. Creates nothing in the CRM.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var result = await settings.TestAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/clinic-crm/treatments — the clinic's current treatments.</summary>
    [HttpGet("treatments")]
    public async Task<IActionResult> GetTreatments(CancellationToken ct)
    {
        var result = await settings.GetTreatmentsAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/clinic-crm/mappings — which clinic treatment each service books as.</summary>
    [HttpPut("mappings")]
    public async Task<IActionResult> SaveMappings(SaveClinicCrmMappingsRequest request, CancellationToken ct)
    {
        var result = await settings.SaveMappingsAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
