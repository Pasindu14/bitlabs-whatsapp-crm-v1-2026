using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Calendar.Dtos;

/// <summary>A bookable service as returned to the client.</summary>
public record AppointmentTypeResponse(
    Guid Id,
    string Name,
    string? Description,
    int DurationMinutes,
    int SlotIntervalMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int MinimumNoticeMinutes,
    int BookingHorizonDays,
    decimal? Price,
    bool IsEnabled,
    bool IsActive,
    DateTime CreatedAt
);

/// <summary>
/// Body for POST /appointment-types. The ranges are deliberately generous but bounded — a 0-minute
/// duration or a 5-year horizon are configuration mistakes, not use cases, and the availability
/// calculator would otherwise be asked to expand them.
/// </summary>
public record CreateAppointmentTypeRequest(
    [Required][MaxLength(120)] string Name,
    [MaxLength(500)] string? Description,
    [Range(5, 1440)] int DurationMinutes,
    [Range(5, 1440)] int SlotIntervalMinutes,
    [Range(0, 480)] int BufferBeforeMinutes,
    [Range(0, 480)] int BufferAfterMinutes,
    [Range(0, 43200)] int MinimumNoticeMinutes,
    [Range(1, 365)] int BookingHorizonDays,
    [Range(0, 9999999)] decimal? Price
);

/// <summary>Body for PUT /appointment-types/{id}. Same fields plus the enable flag.</summary>
public record UpdateAppointmentTypeRequest(
    [Required][MaxLength(120)] string Name,
    [MaxLength(500)] string? Description,
    [Range(5, 1440)] int DurationMinutes,
    [Range(5, 1440)] int SlotIntervalMinutes,
    [Range(0, 480)] int BufferBeforeMinutes,
    [Range(0, 480)] int BufferAfterMinutes,
    [Range(0, 43200)] int MinimumNoticeMinutes,
    [Range(1, 365)] int BookingHorizonDays,
    [Range(0, 9999999)] decimal? Price,
    bool IsEnabled
);
