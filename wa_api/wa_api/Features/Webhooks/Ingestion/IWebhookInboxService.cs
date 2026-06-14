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

    /// <summary>
    /// Atomically claims a non-terminal row: a single conditional UPDATE flips Received/Failed
    /// (or a Processing row whose <paramref name="lease"/> has elapsed — its worker presumed dead)
    /// to Processing and increments Attempts. Returns <c>true</c> when this caller won the claim,
    /// <c>false</c> when another worker already holds a live lease (or the row is terminal) — in which
    /// case the caller must NOT dispatch. Replaces the old non-atomic mark so two concurrent workers
    /// can't both process the same event.
    /// </summary>
    Task<bool> TryClaimForProcessingAsync(WhatsAppWebhookEvent row, TimeSpan lease, CancellationToken ct = default);
    Task MarkProcessedAsync(WhatsAppWebhookEvent row, CancellationToken ct = default);

    /// <summary>Records a failed attempt. <paramref name="dead"/> = retries exhausted (terminal dead-letter).</summary>
    Task MarkFailedAsync(WhatsAppWebhookEvent row, string error, bool dead, CancellationToken ct = default);

    /// <summary>Non-terminal rows last touched before the cutoff — for the sweeper to re-enqueue.</summary>
    Task<IReadOnlyList<Guid>> GetStuckIdsAsync(TimeSpan olderThan, int max, CancellationToken ct = default);
}
