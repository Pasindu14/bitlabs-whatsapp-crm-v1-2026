using System.ComponentModel.DataAnnotations;
using wa_api.Features.Calendar.Entities;

namespace wa_api.Features.Calendar.Dtos;

/// <summary>
/// One offerable slot. Only the customer-facing range is exposed: the occupied range (with buffers) is
/// recomputed server-side at booking time from the appointment type, so a client cannot widen or narrow
/// what a booking actually reserves.
/// </summary>
public record AvailabilitySlotResponse(
    DateTime StartUtc,
    DateTime EndUtc
);

/// <summary>
/// Result of an availability query. <see cref="TimeZoneId"/> travels with the slots because the times
/// are meaningless to render without it — and because the customer must be told which zone they are in.
/// </summary>
public record AvailabilityResponse(
    string TimeZoneId,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<AvailabilitySlotResponse> Slots
);

/// <summary>A booking as returned to the client. All timestamps are UTC.</summary>
public record AppointmentResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    string ContactPhone,
    Guid AppointmentTypeId,
    string AppointmentTypeName,
    Guid? ConversationId,
    DateTime StartUtc,
    DateTime EndUtc,
    AppointmentStatus Status,
    AppointmentSource Source,
    string? Notes,
    DateTime? HoldExpiresAt,
    DateTime? CancelledAt,
    string? CancellationReason,
    DateTime CreatedAt,
    ExternalSyncStatus ExternalSyncStatus = ExternalSyncStatus.NotApplicable,
    int? ExternalBookingId = null,
    string? ExternalSyncError = null
);

/// <summary>
/// Body for POST /appointments. Only the START is accepted — the end is derived from the appointment
/// type's duration so a client can never book a two-hour slot against a thirty-minute service.
/// </summary>
public record CreateAppointmentRequest(
    [Required] Guid ContactId,
    [Required] Guid AppointmentTypeId,
    DateTime StartUtc,
    [MaxLength(1000)] string? Notes,
    Guid? ConversationId
);

/// <summary>How a mirrored booking relates to the clinic's CRM (plan 014). Never sent by a client.</summary>
public record ExternalBookingLink(
    ExternalSyncStatus Status,
    int? BookingId = null,
    int? CustomerId = null,
    string? Error = null
);

/// <summary>Body for PUT /appointments/{id}/reschedule. Buffers are recomputed under current rules.</summary>
public record RescheduleAppointmentRequest(
    DateTime StartUtc
);

/// <summary>Body for POST /appointments/{id}/cancel.</summary>
public record CancelAppointmentRequest(
    [MaxLength(500)] string? Reason
);

/// <summary>
/// Body for POST /appointments/{id}/status — the attendance outcome. Restricted to the terminal
/// outcomes; cancelling has its own endpoint because it also records a reason and frees the slot.
/// </summary>
public record UpdateAppointmentStatusRequest(
    [Required] AppointmentStatus Status
);
