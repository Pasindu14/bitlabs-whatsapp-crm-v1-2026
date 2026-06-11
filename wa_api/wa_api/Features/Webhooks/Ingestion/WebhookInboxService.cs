using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Webhooks.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Webhooks.Ingestion;

public sealed class WebhookInboxService(AppDbContext db) : IWebhookInboxService
{
    private const int MaxError = 4000;

    public async Task<(Guid Id, bool IsNew)> PersistAsync(
        string rawJson, string? phoneNumberId, CancellationToken ct = default)
    {
        var signature = ComputeSignature(rawJson);

        var existing = await FindBySignatureAsync(signature, ct);
        if (existing is not null)
            return (existing.Value, false);

        var row = new WhatsAppWebhookEvent
        {
            ReceivedAt = DateTime.UtcNow,
            PayloadJson = rawJson,
            Status = WebhookEventStatus.Received,
            Attempts = 0,
            PhoneNumberId = phoneNumberId,
            EventSignature = signature,
        };
        db.WhatsAppWebhookEvents.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
            return (row.Id, true);
        }
        catch (DbUpdateException)
        {
            // A concurrent identical delivery won the partial-unique signature index — treat as a replay.
            db.Entry(row).State = EntityState.Detached;
            var dup = await FindBySignatureAsync(signature, ct);
            if (dup is not null) return (dup.Value, false);
            throw;
        }
    }

    public Task<WhatsAppWebhookEvent?> GetForProcessingAsync(Guid id, CancellationToken ct = default)
        => db.WhatsAppWebhookEvents.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<bool> TryClaimForProcessingAsync(
        WhatsAppWebhookEvent row, TimeSpan lease, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var leaseCutoff = now - lease;

        // Atomic compare-and-set: the WHERE predicate IS the lock. Only Received/Failed rows, or a
        // Processing row whose lease has expired, match — so two concurrent workers can't both claim,
        // and a crashed worker's row becomes reclaimable once the lease elapses. ExecuteUpdate commits
        // immediately (its own statement), which is required for the claim to be visible to rivals.
        var affected = await db.WhatsAppWebhookEvents.IgnoreQueryFilters()
            .Where(e => e.Id == row.Id
                && (e.Status == WebhookEventStatus.Received
                 || e.Status == WebhookEventStatus.Failed
                 || (e.Status == WebhookEventStatus.Processing && e.UpdatedAt < leaseCutoff)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, WebhookEventStatus.Processing)
                .SetProperty(e => e.Attempts, e => e.Attempts + 1)
                .SetProperty(e => e.UpdatedAt, now), ct);

        if (affected == 0)
            return false;

        // ExecuteUpdate bypasses the change tracker, so sync the tracked entity: otherwise the later
        // MarkProcessedAsync SaveChanges would write Attempts back to its loaded value (off-by-one on
        // the dead-letter check) and dispatch/logging would see stale state.
        row.Status = WebhookEventStatus.Processing;
        row.Attempts += 1;
        row.UpdatedAt = now;
        return true;
    }

    public async Task MarkProcessedAsync(WhatsAppWebhookEvent row, CancellationToken ct = default)
    {
        row.Status = WebhookEventStatus.Processed;
        row.ProcessedAt = DateTime.UtcNow;
        row.Error = null;
        await db.SaveChangesAsync(ct);
    }

    public Task MarkFailedAsync(WhatsAppWebhookEvent row, string error, bool dead, CancellationToken ct = default)
    {
        var truncated = error.Length > MaxError ? error[..MaxError] : error;
        var now = DateTime.UtcNow;
        // ExecuteUpdate runs a direct SQL UPDATE that bypasses the change tracker, so any PARTIAL handler
        // mutations still pending on the shared context are NOT flushed — only the row's failure state is
        // persisted. The failed batch is then discarded when the scope disposes (a retry re-applies it).
        return db.WhatsAppWebhookEvents.IgnoreQueryFilters()
            .Where(e => e.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, dead ? WebhookEventStatus.Dead : WebhookEventStatus.Failed)
                .SetProperty(e => e.Error, truncated)
                .SetProperty(e => e.ProcessedAt, dead ? now : (DateTime?)null)
                .SetProperty(e => e.UpdatedAt, now), ct);
    }

    public async Task<IReadOnlyList<Guid>> GetStuckIdsAsync(TimeSpan olderThan, int max, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        return await db.WhatsAppWebhookEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => (e.Status == WebhookEventStatus.Received
                      || e.Status == WebhookEventStatus.Processing
                      || e.Status == WebhookEventStatus.Failed)
                     && e.UpdatedAt < cutoff)
            .OrderBy(e => e.UpdatedAt)
            .Take(max)
            .Select(e => e.Id)
            .ToListAsync(ct);
    }

    private async Task<Guid?> FindBySignatureAsync(string signature, CancellationToken ct)
    {
        var match = await db.WhatsAppWebhookEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.EventSignature == signature)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
        return match;
    }

    private static string ComputeSignature(string rawJson)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
}
