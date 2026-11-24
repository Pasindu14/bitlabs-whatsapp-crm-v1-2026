using Hangfire;
using wa_api.Common.Tenancy;
using wa_api.Features.ClinicCrm.Client;

namespace wa_api.Features.ClinicCrm.Jobs;

/// <summary>
/// Tells the clinic's CRM that a customer just sent a meaningful WhatsApp message (POST /messages).
/// <para>
/// Enqueued by the inbound webhook only when the company switched "Notify the CRM about new messages" on and
/// the message passed <see cref="ClinicCrmMessageRelevance"/>. Re-checks the switch when it runs, so turning it
/// off stops jobs already in the queue.
/// </para>
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
[Queue(wa_api.Common.Extensions.HangfireExtensions.BotQueue)]
public sealed class ClinicCrmMessageJob(
    ScopedTenantContext tenant,
    IClinicCrmBookingService crm,
    ILogger<ClinicCrmMessageJob> logger)
{
    public async Task NotifyAsync(Guid companyId, string phone, string? name, CancellationToken ct)
    {
        tenant.EnterCompany(companyId);

        var endpoint = await crm.GetMessageNotifyEndpointAsync(ct);
        if (endpoint is null) return;   // switched off (or disconnected) since the message arrived

        var result = await crm.NotifyMessageAsync(endpoint, phone, ct);

        // The CRM only knows numbers it has saved. The lead job runs alongside this one and may not have
        // finished yet, so save the customer here and try once more.
        if (result.Outcome == ClinicCrmOutcome.NotFound)
        {
            var saved = await crm.SaveInterestedCustomerAsync(
                endpoint, string.IsNullOrWhiteSpace(name) ? ClinicCrmFormat.WhatsApp(phone) : name, phone, ct);
            if (saved.IsOk) result = await crm.NotifyMessageAsync(endpoint, phone, ct);
        }

        switch (result.Outcome)
        {
            case ClinicCrmOutcome.Ok:
                logger.LogInformation("Told the clinic CRM about a new message from {Phone}.", phone);
                return;

            case ClinicCrmOutcome.Unavailable:
                // Hangfire retries a thrown job on the schedule above.
                throw new InvalidOperationException(
                    $"Clinic CRM unreachable while notifying a message from {phone}: {result.Message}");

            default:
                // Bad number or revoked credentials: a retry will not help.
                logger.LogWarning("Clinic CRM refused the message notification for {Phone} ({Outcome}): {Message}",
                    phone, result.Outcome, result.Message);
                return;
        }
    }
}
