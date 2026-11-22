using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;

namespace wa_api.Features.Calendar.Controllers;

/// <summary>
/// Bookings on the company's shared calendar. COMPANY-SCOPED and gated on <c>manage_calendar</c>.
/// <para>
/// Every booking made here is attributed to <see cref="AppointmentSource.Admin"/> — a human acting
/// directly on the calendar. The chatbot books through the service layer with
/// <see cref="AppointmentSource.Bot"/>, which applies the stricter offerability rules that a customer-
/// facing booking must obey.
/// </para>
/// </summary>
[ApiController]
[Route("appointments")]
[Authorize]
[RequirePermission(Permission.ManageCalendar)]
[RequirePlanFeature(Permission.ManageCalendar)]
public class AppointmentsController(IAppointmentService service) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>The acting user, from the token's "sub" claim. Null only if the claim is malformed.</summary>
    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    /// <summary>
    /// GET /api/v1/appointments — bookings overlapping a UTC window. This is what the calendar grid
    /// loads for the visible week or day.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRange(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] bool includeCancelled = false,
        CancellationToken ct = default)
    {
        var result = await service.GetRangeAsync(from, to, includeCancelled, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/appointments/{id} — single booking.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/appointments — book a slot. Returns 409 SLOT_TAKEN if it was just filled.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, AppointmentSource.Admin, CurrentUserId, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/appointments/{id}/reschedule — move a booking to a new start.</summary>
    [HttpPut("{id:guid}/reschedule")]
    public async Task<IActionResult> Reschedule(Guid id, RescheduleAppointmentRequest request, CancellationToken ct)
    {
        var result = await service.RescheduleAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/appointments/{id}/cancel — cancel and free the slot.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelAppointmentRequest request, CancellationToken ct)
    {
        var result = await service.CancelAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/appointments/{id}/status — record the attendance outcome.</summary>
    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateAppointmentStatusRequest request, CancellationToken ct)
    {
        var result = await service.UpdateStatusAsync(id, request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
