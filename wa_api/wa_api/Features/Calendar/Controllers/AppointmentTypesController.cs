using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Features.Calendar.Dtos;

namespace wa_api.Features.Calendar.Controllers;

/// <summary>
/// The bookable services a company offers. COMPANY-SCOPED: every endpoint runs as the caller's company
/// (CompanyId from the JWT) and needs the <c>manage_calendar</c> permission — CompanyAdmin holds it
/// implicitly, an Agent only if it was granted.
/// </summary>
[ApiController]
[Route("appointment-types")]
[Authorize]
[RequirePermission(Permission.ManageCalendar)]
[RequirePlanFeature(Permission.ManageCalendar)]
public class AppointmentTypesController(IAppointmentTypeService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/appointment-types — all types; <c>enabledOnly</c> narrows to what is bookable now.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool enabledOnly = false, CancellationToken ct = default)
    {
        var result = await service.GetAllAsync(enabledOnly, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/appointment-types/{id} — single type.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/appointment-types — add a bookable service.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentTypeRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/appointment-types/{id} — edit a service. Existing bookings are unaffected.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateAppointmentTypeRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/appointment-types/{id} — retire a service (soft-delete).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }
}
