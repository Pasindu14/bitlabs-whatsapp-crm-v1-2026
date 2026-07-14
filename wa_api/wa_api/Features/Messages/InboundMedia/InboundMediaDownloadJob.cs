using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Messages.InboundMedia;

/// <summary>
/// Downloads an inbound media message's bytes from Meta and persists them to <see cref="MessageMedia"/>, then
/// pushes a realtime "media ready" event so the open thread swaps its placeholder for the real image.
/// <para>
/// Runs OUT of the webhook batch transaction (enqueued by <c>InboundMessageWebhookHandler</c>): the webhook
/// dispatch commits inside an explicit DB transaction, and doing two slow Meta HTTP calls there would pin a
/// pooled connection across network I/O (against the small Supavisor cap) and could be re-run by the retry
/// strategy. This job runs with NO tenant context, so it reads with <c>IgnoreQueryFilters()</c> and stamps
/// <c>CompanyId</c> explicitly — mirroring the webhook handler. Idempotent (skips once downloaded) and
/// self-healing: a message not yet visible (racing the webhook commit) throws so Hangfire retries; a
/// genuinely-absent message (rolled-back batch) simply dead-letters after its retries, harmlessly.
/// </para>
/// </summary>
public sealed class InboundMediaDownloadJob(
    AppDbContext db,
    IInboundMediaDownloader downloader,
    IChatNotifier notifier,
    ILogger<InboundMediaDownloadJob> logger)
{
    // Runs on the "default" queue (not "webhooks") so a slow media fetch never starves webhook-processing latency.
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 5, 15, 60, 300, 900 })]
    public async Task RunAsync(Guid messageId, CancellationToken ct)
    {
        var message = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == messageId, ct)
            // Not yet committed (racing the webhook transaction) or rolled back. Throw → Hangfire retries;
            // by the first retry the transaction has committed, or the row never existed and this dead-letters.
            ?? throw new InvalidOperationException($"Message {messageId} not found yet for media download.");

        if (message.MediaDownloadedAt is not null)
            return;                                   // already downloaded — idempotent no-op
        if (string.IsNullOrWhiteSpace(message.MetaMediaId))
            return;                                   // not a media message / nothing to fetch

        var connection = await db.WabaConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Id == message.WabaConnectionId, ct);
        if (connection is null)
        {
            logger.LogWarning("WABA connection {Id} gone — cannot download media for message {MessageId}.",
                message.WabaConnectionId, messageId);
            return;                                   // unrecoverable, not worth retrying
        }

        // EncryptedAccessToken is decrypted on read by the DbContext value converter (same as the sender).
        var downloaded = await downloader.DownloadAsync(message.MetaMediaId!, connection.EncryptedAccessToken, ct)
            ?? throw new InvalidOperationException(
                $"Media download failed for message {messageId} (media {message.MetaMediaId}).");  // throw → retry

        db.MessageMedia.Add(new MessageMedia
        {
            CompanyId = message.CompanyId,            // stamped explicitly — no tenant context in the job
            MessageId = message.Id,
            ContentType = downloaded.ContentType,
            Data = downloaded.Data,
        });
        message.MediaDownloadedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Downloaded {Bytes} bytes of {Type} media for message {MessageId}.",
            downloaded.Data.Length, message.MediaType, messageId);

        await NotifyMediaReadyAsync(message, ct);
    }

    /// <summary>
    /// Best-effort realtime nudge: the client keys messages by id, so it just needs a "message" event for this
    /// conversation to refetch the thread and render the now-available image. Never fails the job.
    /// </summary>
    private async Task NotifyMediaReadyAsync(Message message, CancellationToken ct)
    {
        if (message.ConversationId is null) return;

        try
        {
            var conv = await db.Conversations.IgnoreQueryFilters()
                .Include(c => c.Contact)
                .Include(c => c.WabaConnection)
                .FirstOrDefaultAsync(c => c.Id == message.ConversationId, ct);
            if (conv is null) return;

            var conversationDto = new ConversationResponse(
                conv.Id, conv.ContactId, conv.Contact.Name, conv.Contact.Phone,
                conv.WabaConnectionId, conv.WabaConnection.DisplayPhoneNumber, conv.Status,
                conv.LastMessageAt, conv.LastMessageBody, conv.LastMessageDirection,
                conv.UnreadCount, conv.WindowExpiresAt, conv.CreatedAt);

            var messageDto = new ConversationMessageResponse(
                message.Id, message.ConversationId, message.Body, message.Direction, message.Status,
                message.ExternalMessageId, message.ErrorMessage, message.CreatedAt, message.StatusAt,
                message.MediaType, message.MediaMimeType, MediaReady: true);

            await notifier.MessageAsync(message.CompanyId, conversationDto, messageDto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "media-ready realtime push failed for message {MessageId} — non-fatal.", message.Id);
        }
    }
}
