using Hangfire;
using wa_api.Common.Tenancy;
using wa_api.Features.ClinicCrm.Client;

namespace wa_api.Features.ClinicCrm.Jobs;

/// <summary>
/// Saves a contact who has just written in as an interested customer in the clinic's CRM, with no
/// treatment (the API document's "save every new contact").
/// <para>
/// Runs after the webhook batch commits and takes the number and name as arguments, so it never waits on
/// our own rows. A company without an enabled connection is skipped silently.
/// </para>
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
[Queue(wa_api.Common.Extensions.HangfireExtensions.BotQueue)]
public sealed class ClinicCrmLeadJob(
    ScopedTenantContext tenant,
    IClinicCrmBookingService crm,
    ILogger<ClinicCrmLeadJob> logger)
{
    public async Task SaveAsync(Guid companyId, string phone, string? name, CancellationToken ct)
    {
        tenant.EnterCompany(companyId);

        var endpoint = await crm.GetEndpointAsync(ct);
        if (endpoint is null) return;

        var result = await crm.SaveInterestedCustomerAsync(
            endpoint, string.IsNullOrWhiteSpace(name) ? $"+{phone}" : name, phone, ct);

        switch (result.Outcome)
        {
            case ClinicCrmOutcome.Ok:
                logger.LogInformation("Saved {Phone} in the clinic CRM as customer {CustomerId} ({Action}).",
                    phone, result.Data?.CustomerId, result.Data?.Action);
                return;

            case ClinicCrmOutcome.Unavailable:
                // Worth another go: Hangfire retries a thrown job on the schedule above.
                throw new InvalidOperationException(
                    $"Clinic CRM unreachable while saving {phone}: {result.Message}");

            default:
                // Bad number, revoked credentials: retrying will not help. The
                // connection's health on the settings screen already records the credential failures.
                logger.LogWarning("Clinic CRM would not save {Phone} as a customer ({Outcome}): {Message}",
                    phone, result.Outcome, result.Message);
                return;
        }
    }
}
