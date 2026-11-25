using Microsoft.EntityFrameworkCore;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Messages.OutboundMedia;

/// <summary>
/// Daily sweep that deletes media files older than <c>Media:RetentionDays</c> (default 90) — both files sent
/// from the inbox (on disk) and files received from customers (<c>bytea</c> in Postgres). The message itself
/// and its caption stay; <see cref="Message.MediaPurgedAt"/> is stamped so the inbox shows "file expired"
/// instead of a broken image. Bounded per batch so a large backlog drains over a few runs.
/// </summary>
public sealed class MediaRetentionJob(
    IServiceScopeFactory scopeFactory,
    IMediaStorage storage,
    IConfiguration config,
    ILogger<MediaRetentionJob> logger)
{
    public const int DefaultRetentionDays = 90;
    private const int BatchSize = 200;
    private const int MaxBatchesPerRun = 50;   // ≤ 10k files/run — a safety ceiling, not a normal load

    public async Task RunAsync(CancellationToken ct = default)
    {
        var days = config.GetValue<int?>("Media:RetentionDays") ?? DefaultRetentionDays;
        if (days <= 0) return;   // 0 / negative = keep forever

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var purged = 0;

        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            // Project only ids + path so the blob column is never pulled into memory.
            var rows = await db.MessageMedia.IgnoreQueryFilters()
                .Where(m => m.CreatedAt < cutoff)
                .OrderBy(m => m.CreatedAt)
                .Take(BatchSize)
                .Select(m => new { m.Id, m.MessageId, m.StoragePath })
                .ToListAsync(ct);

            if (rows.Count == 0) break;

            foreach (var row in rows.Where(r => r.StoragePath is not null))
            {
                try { storage.Delete(row.StoragePath!); }
                catch (Exception ex)
                {
                    // A file we can't remove must not stall the sweep — it is retried by nothing, but the
                    // row goes too, so log the path for manual cleanup.
                    logger.LogWarning(ex, "Could not delete expired media file {Path}.", row.StoragePath);
                }
            }

            var messageIds = rows.Select(r => r.MessageId).ToList();
            var messages = await db.Messages.IgnoreQueryFilters()
                .Where(m => messageIds.Contains(m.Id))
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var message in messages) message.MediaPurgedAt = now;

            foreach (var row in rows)
            {
                var stub = new MessageMedia { Id = row.Id };
                db.MessageMedia.Attach(stub);
                db.MessageMedia.Remove(stub);
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            purged += rows.Count;

            if (rows.Count < BatchSize) break;   // caught up
        }

        if (purged > 0)
            logger.LogInformation("MediaRetentionJob removed {Count} media file(s) older than {Days} days.", purged, days);
    }
}
