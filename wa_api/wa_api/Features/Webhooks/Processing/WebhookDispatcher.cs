using System.Text.Json;
using wa_api.Features.Webhooks.Entities;
using wa_api.Features.Webhooks.Handlers;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Features.WhatsApp;

namespace wa_api.Features.Webhooks.Processing;

/// <summary>
/// Routes a received webhook to its handlers. Iterates every <c>entry[].changes[]</c> (a single POST may
/// batch multiple events across phone numbers), resolves the owning connection by phone_number_id, and runs
/// each matching handler. Does NOT save — handlers mutate the shared (scoped) DbContext and the caller
/// commits once, so the batch is atomic. An unknown phone number / id is logged and skipped, never aborting
/// the rest of the batch.
/// </summary>
public sealed class WebhookDispatcher(
    IEnumerable<IWebhookEventHandler> handlers,
    IWabaConnectionService waba,
    ILogger<WebhookDispatcher> logger) : IWebhookDispatcher
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task DispatchAsync(WhatsAppWebhookEvent row, CancellationToken ct = default)
    {
        MetaWebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MetaWebhookEnvelope>(row.PayloadJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            // Malformed JSON can never succeed on retry → surface as a clear, non-retryable failure.
            logger.LogError(ex, "Webhook {Id} payload is not valid JSON.", row.Id);
            throw;
        }

        if (envelope?.Entry is not { Count: > 0 })
        {
            logger.LogWarning("Webhook {Id} had no entries to process.", row.Id);
            return;
        }

        foreach (var entry in envelope.Entry)
        {
            foreach (var change in entry.Changes)
            {
                var field = change.Field;
                if (string.IsNullOrWhiteSpace(field) || change.Value is null)
                    continue;

                var phoneNumberId = change.Value.Metadata?.PhoneNumberId;
                var connection = string.IsNullOrWhiteSpace(phoneNumberId)
                    ? null
                    : await waba.GetConnectionByPhoneNumberIdAsync(phoneNumberId, ct);

                if (!string.IsNullOrWhiteSpace(phoneNumberId) && connection is null)
                    logger.LogWarning("Webhook {Id}: no active connection for phone_number_id {Pnid} (field '{Field}').",
                        row.Id, phoneNumberId, field);

                var ctx = new WebhookContext { Field = field, Change = change, Connection = connection };

                var matched = handlers.Where(h => h.CanHandle(field, change)).ToList();
                if (matched.Count == 0)
                {
                    logger.LogInformation("Webhook {Id}: no handler for field '{Field}' — ignored.", row.Id, field);
                    continue;
                }

                foreach (var handler in matched)
                    await handler.HandleAsync(ctx, ct);
            }
        }
    }
}
