using wa_api.Features.ClinicCrm.Client;

namespace wa_api.Features.ClinicCrm;

/// <summary>How a cancellation sent to the CRM ended.</summary>
public enum ClinicCrmCancelOutcome
{
    /// <summary>Cancelled in the CRM.</summary>
    Cancelled,

    /// <summary>The CRM would not cancel it, so it was flagged "CANCELLATION REQUESTED" for a person there.</summary>
    Flagged,

    /// <summary>Not among the customer's bookings any more — already cancelled or changed at the clinic.</summary>
    NotFound,

    /// <summary>The CRM could not be reached (or refused our credentials). Nothing changed there.</summary>
    Unavailable,
}

public sealed record ClinicCrmCancelResult(ClinicCrmCancelOutcome Outcome, string? Message = null);

/// <summary>
/// What a booking needs to reach the CRM. The start is a UTC instant; the service converts it.
/// <see cref="Notes"/> goes on the booking. The customer is saved without a note. <see cref="TreatmentId"/> is
/// the service's matched clinic treatment; null (a service not matched to one) books with no treatment.
/// </summary>
public sealed record ClinicCrmBookingDraft(
    string CustomerName, string Phone, int? TreatmentId, DateTime StartUtc, string? Notes);

/// <summary>
/// The bot's side of the clinic CRM: whether this company is connected, and the multi-step calls the bot
/// and the retry job both make (save the customer, then book). Tenant-scoped through the DbContext's query
/// filter, so the caller must already have entered the company.
/// </summary>
public interface IClinicCrmBookingService
{
    /// <summary>The zone every CRM date and time is in.</summary>
    TimeZoneInfo CrmTimeZone { get; }

    /// <summary>The current company's enabled connection, or null — null means "book on our own calendar".</summary>
    Task<ClinicCrmEndpoint?> GetEndpointAsync(CancellationToken ct = default);

    /// <summary>
    /// The handover note for a booking — concern, discussed, recommended — or null. Never throws: a booking
    /// goes ahead without a note rather than not at all.
    /// </summary>
    Task<string?> SummarizeConversationAsync(
        Guid companyId, Guid conversationId, string serviceName, CancellationToken ct = default);

    /// <summary>
    /// Saves a contact as an interested customer, with no treatment and no note (POST /customers). Safe to
    /// repeat — the CRM finds them by number.
    /// </summary>
    Task<ClinicCrmResult<ClinicCrmCustomer>> SaveInterestedCustomerAsync(
        ClinicCrmEndpoint endpoint, string name, string phone, CancellationToken ct = default);

    /// <summary>One day's free start times.</summary>
    Task<ClinicCrmResult<ClinicCrmAvailability>> GetAvailabilityAsync(
        ClinicCrmEndpoint endpoint, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Saves the customer (the CRM refuses a booking for an unknown number), then books. A 404 on the
    /// booking saves the customer once more and retries once, as the API document prescribes.
    /// </summary>
    Task<ClinicCrmResult<ClinicCrmBookingChange>> BookAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmBookingDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Every upcoming booking the clinic holds for this number — including ones staff made after a phone
    /// call, which never passed through the chat.
    /// </summary>
    Task<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>> GetUpcomingAsync(
        ClinicCrmEndpoint endpoint, string phone, CancellationToken ct = default);

    /// <summary>Moves a booking. The reply carries the NEW booking id.</summary>
    Task<ClinicCrmResult<ClinicCrmBookingChange>> MoveAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, DateTime newStartUtc, CancellationToken ct = default);

    /// <summary>
    /// Cancels the booking in the CRM (POST /bookings/{id}/cancel). If the CRM refuses to cancel it, flags it
    /// "CANCELLATION REQUESTED" instead so a person there can — a request never just disappears.
    /// </summary>
    Task<ClinicCrmCancelResult> CancelBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, string? reason, CancellationToken ct = default);

    /// <summary>
    /// The nearest thing to a cancel the CRM's API allows: flags the booking with a "CANCELLATION REQUESTED"
    /// note (keeping any note already on it), so it shows on the clinic's screen for a person to cancel.
    /// NotFound when the booking is no longer among the customer's upcoming ones — already cancelled or
    /// changed in the CRM.
    /// </summary>
    Task<ClinicCrmResult<ClinicCrmBookingChange>> RequestCancellationAsync(
        ClinicCrmEndpoint endpoint, int bookingId, string phone, string? reason, CancellationToken ct = default);

    /// <summary>Queues a background push for a booking the CRM could not take just now.</summary>
    void QueueSync(Guid appointmentId);

    /// <summary>An in-app notification for the company's staff. Never throws.</summary>
    Task NotifyStaffAsync(Guid companyId, string title, string body, CancellationToken ct = default);
}
