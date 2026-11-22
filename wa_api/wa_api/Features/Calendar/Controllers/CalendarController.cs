using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Authorization;
using wa_api.Common.Errors;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Features.Calendar.Dtos;

namespace wa_api.Features.Calendar.Controllers;

/// <summary>
/// Calendar configuration — timezone, opening hours, blackouts — and the availability query built from
/// them. COMPANY-SCOPED and gated on <c>manage_calendar</c>.
/// </summary>
[ApiController]
[Route("calendar")]
[Authorize]
[RequirePermission(Permission.ManageCalendar)]
[RequirePlanFeature(Permission.ManageCalendar)]
public class CalendarController(
    ICalendarSettingsService settings,
    IAvailabilityService availability) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/calendar/settings — timezone plus the weekly schedule.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var result = await settings.GetAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/calendar/timezone — set the IANA zone the company operates in.</summary>
    [HttpPut("timezone")]
    public async Task<IActionResult> UpdateTimeZone(UpdateTimeZoneRequest request, CancellationToken ct)
    {
        var result = await settings.UpdateTimeZoneAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>PUT /api/v1/calendar/business-hours — replace the whole weekly schedule.</summary>
    [HttpPut("business-hours")]
    public async Task<IActionResult> SaveBusinessHours(SaveBusinessHoursRequest request, CancellationToken ct)
    {
        var result = await settings.SaveBusinessHoursAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/calendar/blackouts — blocked spans overlapping a UTC window.</summary>
    [HttpGet("blackouts")]
    public async Task<IActionResult> GetBlackouts(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        var result = await settings.GetBlackoutsAsync(from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/calendar/blackouts — block a span regardless of opening hours.</summary>
    [HttpPost("blackouts")]
    public async Task<IActionResult> CreateBlackout(CreateBlackoutRequest request, CancellationToken ct)
    {
        var result = await settings.CreateBlackoutAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ResponseHelper.Created(result, CorrelationId));
    }

    /// <summary>DELETE /api/v1/calendar/blackouts/{id} — unblock a span (soft-delete).</summary>
    [HttpDelete("blackouts/{id:guid}")]
    public async Task<IActionResult> DeleteBlackout(Guid id, CancellationToken ct)
    {
        await settings.DeleteBlackoutAsync(id, ct);
        return Ok(ResponseHelper.Ok<object?>(null, CorrelationId));
    }

    /// <summary>
    /// GET /api/v1/calendar/availability — bookable slots for a service. Dates are the company's LOCAL
    /// calendar dates; both are clamped to the type's booking horizon server-side.
    /// </summary>
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(
        [FromQuery] Guid appointmentTypeId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var result = await availability.GetAsync(appointmentTypeId, from, to, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }
}
