using System.Text.Json;

namespace wa_api.Features.ClinicCrm.Client;

/// <summary>
/// The clinic CRM's WhatsApp BOT API, one method per endpoint. Handles the token, the response envelope
/// and the status codes; returns <see cref="ClinicCrmResult{T}"/> and never throws for anything the CRM
/// answers — only for cancellation.
/// </summary>
public interface IClinicCrmClient
{
    /// <summary>Exchanges the credentials for a token. Used on its own only by "Test connection".</summary>
    Task<ClinicCrmResult<ClinicCrmToken>> GetTokenAsync(ClinicCrmEndpoint endpoint, CancellationToken ct = default);

    /// <summary>GET /treatments.</summary>
    Task<ClinicCrmResult<IReadOnlyList<ClinicCrmTreatment>>> GetTreatmentsAsync(
        ClinicCrmEndpoint endpoint, CancellationToken ct = default);

    /// <summary>POST /customers — creates the customer, or returns the existing one for that number.</summary>
    Task<ClinicCrmResult<ClinicCrmCustomer>> SaveCustomerAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmSaveCustomerRequest request, CancellationToken ct = default);

    /// <summary>GET /availability?date= — the day's free start times in the CRM's local time.</summary>
    Task<ClinicCrmResult<ClinicCrmAvailability>> GetAvailabilityAsync(
        ClinicCrmEndpoint endpoint, DateOnly date, CancellationToken ct = default);

    /// <summary>POST /bookings.</summary>
    Task<ClinicCrmResult<ClinicCrmBookingChange>> BookAsync(
        ClinicCrmEndpoint endpoint, ClinicCrmBookRequest request, CancellationToken ct = default);

    /// <summary>GET /bookings?whatsapp= — the customer's upcoming bookings.</summary>
    Task<ClinicCrmResult<IReadOnlyList<ClinicCrmBooking>>> GetBookingsAsync(
        ClinicCrmEndpoint endpoint, string whatsapp, CancellationToken ct = default);

    /// <summary>
    /// POST /bookings/{id}/cancel — added by the clinic after the first version of their document, so its
    /// reply is read as raw JSON: success is what counts, not the shape of <c>data</c>.
    /// </summary>
    Task<ClinicCrmResult<JsonElement>> CancelBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, ClinicCrmCancelBookingRequest request, CancellationToken ct = default);

    /// <summary>PATCH /bookings/{id}. Moving a booking returns a NEW booking id.</summary>
    Task<ClinicCrmResult<ClinicCrmBookingChange>> UpdateBookingAsync(
        ClinicCrmEndpoint endpoint, int bookingId, ClinicCrmUpdateBookingRequest request, CancellationToken ct = default);
}
