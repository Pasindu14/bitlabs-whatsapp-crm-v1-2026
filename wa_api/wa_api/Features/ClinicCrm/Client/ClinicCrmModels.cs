namespace wa_api.Features.ClinicCrm.Client;

/// <summary>Everything needed to call one clinic's CRM. Built from a <c>ClinicCrmConnection</c> row.</summary>
public sealed record ClinicCrmEndpoint(Guid ConnectionId, string BaseUrl, string ClientId, string ClientSecret);

/// <summary>
/// How a CRM call ended. The expected refusals are values, not exceptions: "that time was just taken" is
/// a conversation the bot can have, and the caller decides what each one means for the customer.
/// </summary>
public enum ClinicCrmOutcome
{
    /// <summary>200 with <c>success: true</c>.</summary>
    Ok,

    /// <summary>400 — bad or out-of-rules input (closed day, past time, unknown treatment). Has a message.</summary>
    Rejected,

    /// <summary>404 — customer not saved yet, or no such booking for this number.</summary>
    NotFound,

    /// <summary>409 — the time is taken or blocked, or the booking can no longer change. May carry free times.</summary>
    Taken,

    /// <summary>401 after a fresh token, or 403 — the credentials are wrong or revoked. Needs an admin.</summary>
    NotAuthorised,

    /// <summary>Timeout, network failure, 429 or 5xx — worth trying again later.</summary>
    Unavailable,
}

/// <summary>The result of one CRM call.</summary>
public sealed record ClinicCrmResult<T>(
    ClinicCrmOutcome Outcome,
    T? Data = default,
    string? Message = null,
    string? ErrorField = null,
    IReadOnlyList<string>? FreeTimes = null)
{
    public bool IsOk => Outcome == ClinicCrmOutcome.Ok;

    /// <summary>The same failure, re-typed — for a call that stops at an earlier step.</summary>
    public ClinicCrmResult<TOther> As<TOther>() => new(Outcome, default, Message, ErrorField, FreeTimes);
}

// ── Wire models. Property names follow the API document; JSON is camelCase. ──────────────────────

public sealed record ClinicCrmToken(string AccessToken, string? TokenType, int ExpiresIn);

public sealed record ClinicCrmTreatment(int Id, string Name, string? Description);

public sealed record ClinicCrmCustomer(int CustomerId, string? Action, string? CustomerName, string? Stage);

/// <summary>A day's free start times, in the CRM's local time (<c>HH:mm</c>).</summary>
public sealed record ClinicCrmAvailability(
    string Date, bool Open, string? OpensAt, string? ClosesAt, int DurationMinutes, IReadOnlyList<string>? FreeTimes);

public sealed record ClinicCrmBookingTreatment(int Id, string Name);

public sealed record ClinicCrmBooking(
    int BookingId,
    int? CustomerId,
    string? CustomerName,
    string Date,
    string StartTime,
    string? EndTime,
    IReadOnlyList<ClinicCrmBookingTreatment>? Treatments,
    string? Status,
    string? Notes);

/// <summary>What POST /bookings and PATCH /bookings/{id} return.</summary>
public sealed record ClinicCrmBookingChange(string? Action, int? PreviousBookingId, ClinicCrmBooking Booking);

// ── Request bodies ────────────────────────────────────────────────────────────────────────────

public sealed record ClinicCrmSaveCustomerRequest(
    string Name, string Whatsapp, IReadOnlyList<int> TreatmentIds, string? Notes);

public sealed record ClinicCrmBookRequest(
    string Whatsapp, IReadOnlyList<int> TreatmentIds, string Date, string StartTime, string? Notes);

/// <summary>
/// Body for POST /bookings/{id}/cancel. The number must match the booking's customer; <see cref="Note"/> is
/// recorded with the cancellation (up to 500 characters).
/// </summary>
public sealed record ClinicCrmCancelBookingRequest(string Whatsapp, string? Note);

/// <summary>Body for POST /messages: the number (with <c>+</c>) that just wrote in.</summary>
public sealed record ClinicCrmMessageRequest(string Whatsapp);

/// <summary>PATCH body. Null fields are left out of the JSON, so only what changes is sent.</summary>
public sealed record ClinicCrmUpdateBookingRequest(
    string Whatsapp, string? Date = null, string? StartTime = null,
    IReadOnlyList<int>? TreatmentIds = null, string? Notes = null);
