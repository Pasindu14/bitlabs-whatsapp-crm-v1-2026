using wa_api.Features.Webhooks.Entities;

namespace wa_api.Features.Webhooks.Ingestion;

/// <summary>
/// The transactional inbox for inbound webhooks: durable persistence at ingest + the state transitions
/// the processing job drives. All reads/writes bypass the tenant query filter (the inbox is platform-level
/// and runs under the null-tenant background scope).
/// </summary>
public interface IWebhookInboxService
{
    /// <summary>
    /// Persists a received webhook, deduping replays by payload signature. Returns the row id and whether
    /// it was newly inserted (false → an identical delivery already exists; the caller should NOT re-enqueue).
    /// </summary>
    Task<(Guid Id, bool IsNew)> PersistAsync(string rawJson, string? phoneNumberId, CancellationToken ct = default);

    /// <summary>Loads a row (tracked) for processing. Null when the id is unknown.</summary>
    Task<WhatsAppWebhookEvent?> GetForProcessingAsync(Guid id, CancellationToken ct = default);

    Task MarkProcessingAsync(WhatsAppWebhookEvent row, CancellationToken ct = default);
    Task MarkProcessedAsync(WhatsAppWebhookEvent row, CancellationToken ct = default);

    /// <summary>Records a failed attempt. <paramref name="dead"/> = retries exhausted (terminal dead-letter).</summary>
    Task MarkFailedAsync(WhatsAppWebhookEvent row, string error, bool dead, CancellationToken ct = default);

    /// <summary>Non-terminal rows last touched before the cutoff — for the sweeper to re-enqueue.</summary>
    Task<IReadOnlyList<Guid>> GetStuckIdsAsync(TimeSpan olderThan, int max, CancellationToken ct = default);
}
