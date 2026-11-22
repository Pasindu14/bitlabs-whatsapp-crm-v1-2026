using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Calendar.Dtos;

/// <summary>One opening window, in the company's local wall-clock time.</summary>
public record BusinessHoursResponse(
    Guid Id,
    DayOfWeek DayOfWeek,
    TimeOnly StartLocal,
    TimeOnly EndLocal
);

/// <summary>One window in a submitted weekly schedule.</summary>
public record BusinessHoursItem(
    [Range(0, 6)] DayOfWeek DayOfWeek,
    TimeOnly StartLocal,
    TimeOnly EndLocal
);

/// <summary>
/// Body for PUT /calendar/business-hours — REPLACE-ALL semantics. The weekly schedule is edited as one
/// grid, so submitting the whole thing avoids a per-row diff on the client and makes the request
/// naturally idempotent. Send an empty list to close every day.
/// </summary>
public record SaveBusinessHoursRequest(
    [Required] List<BusinessHoursItem> Windows
);

/// <summary>The company's calendar configuration in one payload — what the settings screen loads.</summary>
public record CalendarSettingsResponse(
    string TimeZoneId,
    IReadOnlyList<BusinessHoursResponse> BusinessHours
);

/// <summary>Body for PUT /calendar/timezone.</summary>
public record UpdateTimeZoneRequest(
    [Required][MaxLength(64)] string TimeZoneId
);

/// <summary>A blocked span. Times are UTC; the client renders them in the company's zone.</summary>
public record BlackoutResponse(
    Guid Id,
    DateTime StartUtc,
    DateTime EndUtc,
    string Reason
);

/// <summary>Body for POST /calendar/blackouts.</summary>
public record CreateBlackoutRequest(
    DateTime StartUtc,
    DateTime EndUtc,
    [Required][MaxLength(200)] string Reason
);
