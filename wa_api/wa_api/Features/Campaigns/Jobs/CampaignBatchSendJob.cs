using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;
using wa_api.Infrastructure.RateLimiting;

namespace wa_api.Features.Campaigns.Jobs;

/// <summary>
/// Processes a batch of Queued CampaignRecipient rows, sending each through the
/// rate limiter and Meta Cloud API (template message send).
///
/// Runs cross-tenant (Hangfire has no HttpContext): loads via IgnoreQueryFilters,
/// stamps CompanyId manually on every Message insert.
///
/// Rate-limit handling is graceful: a throttled send is waited out for sub-second pauses, otherwise the
/// batch is rescheduled (Hangfire Schedule) with jitter — never thrown. So a rate limit can never wedge a
/// campaign. The [AutomaticRetry] bound covers only genuine job faults; on exhaustion the job is deleted and
/// the campaign sweeper re-enqueues any campaign left Running with Queued recipients.
/// </summary>
[AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public class CampaignBatchSendJob(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient jobClient,
    IHttpClientFactory httpClientFactory,
    IOptions<RateLimitOptions> rateOptions,
    ILogger<CampaignBatchSendJob> logger)
{
    private const int BatchSize = 50;

    /// <summary>How long a recipient may sit in the transient <c>Sending</c> claim before a later batch
    /// treats it as abandoned (crashed mid-send) and reclaims it to Queued. Comfortably longer than a single
    /// send's wall-clock (Meta HttpClient timeout is 15s) so a healthy in-flight send is never reclaimed.</summary>
    private static readonly TimeSpan StaleClaimThreshold = TimeSpan.FromMinutes(5);

    /// <summary>Inter-send pause applied only while the number's quality rating is YELLOW, to halve the
    /// effective send rate and ease pressure on an at-risk number (M5). GREEN sends run unthrottled.</summary>
    private static readonly TimeSpan YellowInterSendDelay = TimeSpan.FromMilliseconds(250);
    private readonly RateLimitOptions _rateOptions = rateOptions.Value;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task RunAsync(Guid campaignId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rateLimiter = scope.ServiceProvider.GetRequiredService<IWabaRateLimiter>();
        var notifier = scope.ServiceProvider.GetRequiredService<Notifications.INotificationService>();
        var meter = scope.ServiceProvider.GetRequiredService<Common.Subscriptions.ISubscriptionMeter>();

        var campaign = await db.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.Template).ThenInclude(t => t.WabaConnection)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
        {
            logger.LogWarning("CampaignBatchSendJob: campaign {Id} not found.", campaignId);
            return;
        }

        // Exit early if the campaign was paused or cancelled between enqueue and execution.
        if (campaign.Status is CampaignStatus.Paused or CampaignStatus.Cancelled
            or CampaignStatus.Completed or CampaignStatus.Failed)
        {
            logger.LogInformation("CampaignBatchSendJob: campaign {Id} is {Status}, skipping batch.",
                campaignId, campaign.Status);
            return;
        }

        // Subscription-plane enforcement (M21): the HTTP send gate blocks an inactive / period-ended
        // subscription, but campaigns bypassed it entirely. There is NO auto-renew — a subscription simply
        // runs until CurrentPeriodEnd and the tenant must subscribe again — so a long-running or Recurring
        // campaign that crosses that boundary must stop, not keep sending free/uncounted. Pause + notify when
        // there is no active, in-period subscription. (Quota within the period is still enforced per-send below.)
        var sub = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.CompanyId == campaign.CompanyId
                     && s.Status == wa_api.Features.Subscriptions.Entities.SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.CurrentPeriodEnd })
            .FirstOrDefaultAsync(ct);
        if (sub is null || sub.CurrentPeriodEnd < DateTime.UtcNow)
        {
            campaign.Status = CampaignStatus.Paused;
            await db.SaveChangesAsync(ct);
            await notifier.CreateAsync(
                campaign.CompanyId, campaignId, null,
                NotificationType.CampaignThrottled,
                "Campaign paused — subscription ended",
                $"\"{campaign.Name}\" was paused because your subscription has ended. " +
                "Subscribe again, then resume the campaign.",
                ct);
            logger.LogWarning(
                "CampaignBatchSendJob: campaign {Id} paused — company {CompanyId} has no active in-period subscription.",
                campaignId, campaign.CompanyId);
            return;
        }

        // UAE quiet-hours gate: business-initiated sends are only permitted 08:00–20:00 Asia/Dubai.
        // If we're outside the window, defer the whole batch to the next 08:00 UAE and return —
        // recipients stay Queued so the resumed run continues exactly where this one stopped (no
        // double-send). Same graceful shape as the throttle/daily-tier reschedule paths. This is the
        // guarantee that a long send started before 20:00 never spills past it: each batch re-checks.
        var nowUtc = DateTime.UtcNow;
        if (!SendWindow.IsOpen(nowUtc))
        {
            var resumeJobId = jobClient.Schedule<CampaignBatchSendJob>(
                j => j.RunAsync(campaignId, CancellationToken.None), SendWindow.NextOpen(nowUtc) - nowUtc);
            campaign.HangfireJobId = resumeJobId;
            await db.SaveChangesAsync(ct);

            // Idempotent (unique index on CampaignId+Type) — at most one such notice per campaign.
            await notifier.CreateAsync(
                campaign.CompanyId, campaignId, null,
                NotificationType.CampaignThrottled,
                "Campaign paused — outside UAE sending hours",
                $"\"{campaign.Name}\" is outside the permitted messaging window ({SendWindow.WindowText}) " +
                "and will resume automatically at 8:00 AM.",
                ct);

            logger.LogInformation(
                "CampaignBatchSendJob: campaign {Id} outside UAE send window — deferred to next 08:00 (job {JobId}).",
                campaignId, resumeJobId);
            return;
        }

        var waba = campaign.Template.WabaConnection;

        // Quality rating gate: RED = stop all business-initiated sends; YELLOW = halve throughput.
        if (waba.QualityRating == "RED")
        {
            logger.LogWarning(
                "WABA {WabaId} quality rating is RED — pausing campaign {CampaignId} to protect account standing.",
                waba.Id, campaignId);

            campaign.Status = CampaignStatus.Paused;
            await db.SaveChangesAsync(ct);

            await notifier.CreateAsync(
                campaign.CompanyId, campaignId, null,
                NotificationType.CampaignThrottled,
                "Campaign paused — low WhatsApp quality rating",
                "Your WhatsApp number has a RED quality rating. Campaign paused to prevent account restriction. " +
                "Improve message quality and opt-in practices in Meta Business Suite, then resume the campaign.",
                ct);
            return;
        }

        // YELLOW quality: reduce send PRESSURE while quality recovers. Halving the batch alone is ineffective
        // (M5) — batches chain back-to-back, so real throughput is governed by the per-second limiter, not the
        // batch size. So we also inject an inter-send delay (YellowInterSendDelay) after each send below to
        // genuinely lower the effective rate for an at-risk number. The smaller batch just tightens the
        // reschedule cadence so a RED downgrade / recovery is noticed sooner.
        var isYellow = waba.QualityRating == "YELLOW";
        var effectiveBatchSize = isYellow ? BatchSize / 2 : BatchSize;

        // The number's 24-hour unique-recipient tier cap (campaign sends are business-initiated).
        var tierLimit = (int)waba.MessagingTier;

        // Crash recovery: reclaim any recipient a crashed batch left claimed (Sending) longer than the
        // stale window back to Queued so this run can re-send it. Only STALE rows (UpdatedAt older than the
        // threshold) are touched — a recipient a concurrently-running batch is actively sending right now is
        // fresh and left alone, so this can never resurrect an in-flight send. Atomic, tenant-filter-free.
        await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Where(r => r.CampaignId == campaignId
                        && r.Status == RecipientStatus.Sending
                        && r.UpdatedAt < DateTime.UtcNow - StaleClaimThreshold)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecipientStatus.Queued), ct);

        // Load one batch of Queued recipients with their contacts.
        var batch = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Include(r => r.Contact)
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .Take(effectiveBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            // All recipients processed.
            campaign.CompletedAt = DateTime.UtcNow;
            if (campaign.ScheduleType == ScheduleType.Recurring)
            {
                campaign.Status = CampaignStatus.Scheduled;
                logger.LogInformation(
                    "CampaignBatchSendJob: recurring campaign {Id} complete — reset to Scheduled.", campaignId);
            }
            else
            {
                campaign.Status = CampaignStatus.Completed;
                campaign.HangfireJobId = null;
                logger.LogInformation("CampaignBatchSendJob: campaign {Id} completed.", campaignId);
            }
            await db.SaveChangesAsync(ct);
            if (campaign.Status == CampaignStatus.Completed)
                await NotifyCompletedAsync(campaign, db, notifier, ct);
            return;
        }

        var resolvedMapping = ResolveMapping(campaign.VariableMapping);

        // Idempotency guard: of THIS batch's contacts, which already have a message in this campaign
        // (from a prior, possibly interrupted, run). A recipient here was sent before — never send again.
        // This closes the duplicate-message window where Meta accepted a send but the status flip wasn't
        // committed before the job was retried/re-enqueued. Scoped to the batch so it stays cheap at scale.
        // Scoped to the CURRENT run: a Recurring campaign re-sends to the same contacts each run, so a
        // message from a prior run must NOT suppress this run's send. Only messages stamped with this run
        // count as "already sent" (existing pre-run campaign messages are backfilled to run 1 by migration).
        var batchContactIds = batch.Select(r => r.ContactId).ToList();
        var alreadyMessaged = new HashSet<Guid>(
            await db.Messages.IgnoreQueryFilters()
                .Where(m => m.CampaignId == campaignId
                            && m.CampaignRunNumber == campaign.CurrentRunNumber
                            && batchContactIds.Contains(m.ContactId))
                .Select(m => m.ContactId)
                .ToListAsync(ct));

        // ── Resolve a media (image/video/document) header ONCE per batch ──────────────────────
        // A template with a media header must re-supply the actual media on EVERY send. The template's
        // stored MediaHandle is a resumable-upload handle valid only for template creation, not for
        // message sends — so we upload the persisted sample bytes to the phone number's /media endpoint
        // and reuse the returned media id across this batch's recipients.
        // headerMediaRequired with a null headerMedia means resolution failed: every send would 400, so
        // we fail those recipients locally instead of burning Meta round-trips.
        var header = campaign.Template.Components?.Header;
        var headerMediaRequired = header is not null && header.Type is "image" or "video" or "document";
        (string Type, string MediaId)? headerMedia = null;
        if (headerMediaRequired)
        {
            var sample = header!.MediaPreviewId is { } sampleId
                ? await db.TemplateMediaSamples.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Id == sampleId, ct)
                : null;

            if (sample is null)
            {
                logger.LogError(
                    "Campaign {Id}: template header is '{Type}' but no stored media sample was found — cannot send.",
                    campaignId, header.Type);
            }
            else
            {
                var mediaId = await UploadHeaderMediaAsync(waba.PhoneNumberId, waba.EncryptedAccessToken, sample, ct);
                if (mediaId is not null)
                    headerMedia = (header.Type, mediaId);
            }
        }

        foreach (var recipient in batch)
        {
            // Re-check campaign status inside loop — pause/cancel can arrive mid-batch.
            if (campaign.Status is CampaignStatus.Paused or CampaignStatus.Cancelled)
                break;

            // Consent gate: skip contacts with no opt-in consent UNLESS this campaign explicitly
            // overrides it (operator attests to off-platform consent). The override does NOT bypass
            // the opt-out or not-on-WhatsApp gates below — those remain hard stops.
            var noConsent = !recipient.Contact.HasOptedIn;
            if (noConsent && !campaign.OverrideConsentGate)
            {
                recipient.Status = RecipientStatus.Skipped;
                recipient.ErrorCode = "NO_CONSENT";
                logger.LogDebug(
                    "Skipping contact {ContactId} in campaign {CampaignId} — no opt-in consent.",
                    recipient.ContactId, campaignId);
                continue;
            }

            if (recipient.Contact.IsOptedOut)
            {
                recipient.Status = RecipientStatus.Skipped;
                recipient.ErrorCode = "OPT_OUT";
                logger.LogInformation(
                    "Skipping contact {ContactId} in campaign {CampaignId} — opted out.",
                    recipient.ContactId, campaignId);
                continue;
            }

            // Registration gate: a prior send/webhook proved this number isn't on WhatsApp (131026).
            // Skip it so we don't waste a send — or a daily tier slot — on a known-dead number.
            if (!recipient.Contact.IsWhatsAppValid)
            {
                recipient.Status = RecipientStatus.Skipped;
                recipient.ErrorCode = "NOT_ON_WHATSAPP";
                logger.LogInformation(
                    "Skipping contact {ContactId} in campaign {CampaignId} — not a WhatsApp user.",
                    recipient.ContactId, campaignId);
                continue;
            }

            // Idempotency: this contact already received a message in this campaign (a prior run sent it
            // but didn't commit the status flip before being retried). Reconcile the status and skip the
            // resend — this is the guard against the duplicate-message bug.
            if (alreadyMessaged.Contains(recipient.ContactId))
            {
                // A message already exists (a prior attempt sent to Meta but didn't commit the flip). Reconcile
                // to Accepted — the "dispatched, awaiting confirmation" state — and let the status webhooks
                // advance it; the forward-only sync never regresses a message already past Accepted.
                recipient.Status = RecipientStatus.Accepted;
                logger.LogWarning(
                    "Recipient {ContactId} in campaign {CampaignId} already has a message — reconciling to Accepted, skipping resend.",
                    recipient.ContactId, campaignId);
                continue;
            }

            // The template needs a media header but we couldn't obtain a send-time media id — every send would
            // fail at Meta with HTTP 400. Fail this recipient locally BEFORE acquiring a permit (M20): the tier
            // permit counts against the number's scarce daily unique-recipient cap, so consuming one for a send
            // that cannot happen would burn that allowance for nothing.
            if (headerMediaRequired && headerMedia is null)
            {
                recipient.Status = RecipientStatus.Failed;
                recipient.ErrorCode = "HEADER_MEDIA_MISSING";
                continue;
            }

            // ── Acquire a send permit (graceful: brief wait, else reschedule the whole batch) ──
            // Per-second pacing + the number's daily unique-recipient tier cap.
            var permit = await rateLimiter.TryAcquireAsync(waba.PhoneNumberId, recipient.ContactId, tierLimit, ct);
            if (!permit.Allowed
                && permit.Reason == RateLimitReason.PerSecondThrottle
                && permit.RetryAfter.TotalMilliseconds <= _rateOptions.CampaignWaitCapMs)
            {
                // The send has NOT happened yet, so waiting then retrying the SAME recipient is duplicate-safe.
                await Task.Delay(permit.RetryAfter, ct);
                permit = await rateLimiter.TryAcquireAsync(waba.PhoneNumberId, recipient.ContactId, tierLimit, ct);
            }
            if (!permit.Allowed)
            {
                // Persist progress (recipient stays Queued) and reschedule — never throw, never stall.
                await db.SaveChangesAsync(ct);
                await RescheduleBatchAsync(campaign, campaignId, permit, db, notifier, ct);
                return;
            }

            // ── Atomic send-claim: the definitive double-send guard ────────────────────────────────
            // Flip THIS recipient Queued→Sending in one atomic UPDATE. If a concurrent batch already
            // claimed (or sent) it, zero rows change and we skip WITHOUT calling Meta — so a recipient can
            // never be sent twice, even if two batch jobs run at once (the exact bug behind the duplicate
            // WhatsApp messages). ExecuteUpdate commits immediately, making the claim visible to rivals; the
            // UpdatedAt stamp lets a later batch reclaim this row if we crash while Sending (see stale sweep).
            var claimed = await db.CampaignRecipients
                .IgnoreQueryFilters()
                .Where(r => r.Id == recipient.Id && r.Status == RecipientStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RecipientStatus.Sending)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);
            if (claimed == 0)
            {
                logger.LogInformation(
                    "Recipient {ContactId} in campaign {CampaignId} already claimed by another batch — skipping.",
                    recipient.ContactId, campaignId);
                continue;
            }
            // ExecuteUpdate bypassed the change tracker — sync the tracked entity so later saves see Sending.
            recipient.Status = RecipientStatus.Sending;

            // Reserve quota atomically BEFORE sending (C1 reserve-then-send): the meter enforces a hard ceiling,
            // so concurrent batches near the cap can't overshoot the plan. Null ⇒ ceiling reached (M21 ensured
            // an active in-period sub at batch start): release this recipient's claim and pause the campaign.
            var reservedSubId = await meter.TryReserveAsync(campaign.CompanyId, 1, ct);
            if (reservedSubId is null)
            {
                // Release the claim with an atomic UPDATE (a tracked Sending→Queued is a no-op vs the loaded
                // snapshot), then pause so a resume re-sends this recipient once quota is available.
                await db.CampaignRecipients.IgnoreQueryFilters()
                    .Where(r => r.Id == recipient.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, RecipientStatus.Queued)
                        .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);
                recipient.Status = RecipientStatus.Queued;
                campaign.Status = CampaignStatus.Paused;
                await db.SaveChangesAsync(ct);

                await notifier.CreateAsync(
                    campaign.CompanyId, campaignId, null,
                    NotificationType.CampaignThrottled,
                    "Campaign paused — message quota reached",
                    $"\"{campaign.Name}\" reached your plan's message quota and was paused. " +
                    "Add credits or upgrade your plan, then resume the campaign.",
                    ct);
                logger.LogWarning(
                    "CampaignBatchSendJob: campaign {Id} paused — company {CompanyId} quota ceiling reached.",
                    campaignId, campaign.CompanyId);
                return;
            }

            var sendSucceeded = false;
            try
            {
                var resolvedVars = BuildResolvedVariables(resolvedMapping, recipient.Contact);
                var (externalId, errorCode, retryAfter) = await SendTemplateMessageAsync(
                    waba.PhoneNumberId, waba.EncryptedAccessToken,
                    recipient.Contact.Phone,
                    campaign.Template.Name, campaign.Template.Language,
                    resolvedVars, headerMedia, ct);

                if (externalId is not null)
                {
                    // Create the Message row manually with CompanyId set (no HttpContext in job scope).
                    var message = new Message
                    {
                        CompanyId = campaign.CompanyId,
                        ContactId = recipient.ContactId,
                        WabaConnectionId = waba.Id,
                        CampaignId = campaignId,
                        Body = BuildBodyPreview(campaign.Template.Components?.Body?.Text, resolvedVars),
                        Direction = MessageDirection.Outbound,
                        // Accepted, not Sent: Meta returned a wamid (queued) but hasn't delivered its 'sent'
                        // status webhook yet. The incoming 'sent' webhook promotes this to Sent, so a message
                        // frozen at Accepted honestly signals a delivery/webhook problem rather than success.
                        Status = MessageStatus.Accepted,
                        ExternalMessageId = externalId,
                        CampaignRunNumber = campaign.CurrentRunNumber,
                        // Quota was reserved up front — stamp the charged sub so a later delivery-failure
                        // webhook refunds that exact row.
                        MeteredSubscriptionId = reservedSubId,
                    };
                    db.Messages.Add(message);

                    // Flip the recipient to Accepted in the SAME save as the message insert, so the send is
                    // recorded atomically. (Previously the status flip lagged to a later save; if the job
                    // was retried in that window the recipient was still Queued and got sent a SECOND time.)
                    // Accepted (not Sent): Meta returned a wamid but its 'sent' webhook is still in flight —
                    // MessageStatusWebhookHandler promotes it to Sent. Setting the Message nav fills MessageId
                    // on insert without a separate round-trip.
                    recipient.Status = RecipientStatus.Accepted;
                    recipient.Message = message;
                    recipient.ResolvedVariables = JsonSerializer.Serialize(resolvedVars, JsonOpts);
                    campaign.SentCount++;

                    // Audit: this send went to a contact without recorded opt-in (override was on).
                    if (noConsent)
                    {
                        recipient.SentWithoutConsent = true;
                        campaign.NoConsentSentCount++;
                    }

                    // Track within this run too, so a contact can never be picked twice in one batch.
                    alreadyMessaged.Add(recipient.ContactId);

                    // One save records the message (with the reserved subscription stamped) and the recipient
                    // flip to Sent atomically. Quota was already reserved before the send, so there's no
                    // separate meter call and no post-hoc ceiling re-check — the reserve enforced the cap.
                    await db.SaveChangesAsync(ct);
                    sendSucceeded = true;
                }
                else
                {
                    // Account-level restriction: deactivate the WABA and halt immediately.
                    if (MetaPolicyErrorCodes.IsAccountRestriction(errorCode))
                    {
                        logger.LogError(
                            "Meta returned account restriction code {Code} for WABA {WabaId} — " +
                            "deactivating connection and halting campaign {CampaignId}.",
                            errorCode, waba.Id, campaignId);

                        waba.IsActive = false;
                        campaign.Status = CampaignStatus.Paused;
                        // The send did not happen — release the claim so a resume re-sends this recipient.
                        recipient.Status = RecipientStatus.Queued;
                        await db.SaveChangesAsync(ct);

                        await notifier.CreateAsync(
                            campaign.CompanyId, campaignId, null,
                            NotificationType.CampaignFailed,
                            "WhatsApp account restricted",
                            $"Meta has restricted your WhatsApp number (code {errorCode}). All campaigns paused. " +
                            "Please review your account in Meta Business Suite before resuming.",
                            ct);
                        return;
                    }

                    // Access token expired/invalid (190): fatal for the whole connection — every remaining send
                    // would fail identically. Deactivate the connection, pause the campaign, release the claim
                    // and halt immediately instead of burning the audience one 190 at a time (H12).
                    if (MetaPolicyErrorCodes.IsAuthError(errorCode))
                    {
                        logger.LogError(
                            "Meta returned auth error {Code} for WABA {WabaId} — deactivating connection and halting campaign {CampaignId}.",
                            errorCode, waba.Id, campaignId);

                        waba.IsActive = false;
                        campaign.Status = CampaignStatus.Paused;
                        recipient.Status = RecipientStatus.Queued;
                        await db.SaveChangesAsync(ct);

                        await notifier.CreateAsync(
                            campaign.CompanyId, campaignId, null,
                            NotificationType.CampaignFailed,
                            "Campaign paused — WhatsApp access token invalid",
                            $"Your WhatsApp connection's access token is expired or invalid (code {errorCode}). " +
                            "All sending is paused. Reconnect the number in settings, then resume the campaign.",
                            ct);
                        return;
                    }

                    // Campaign-level spam/ecosystem signal: pause this campaign and alert.
                    if (MetaPolicyErrorCodes.IsCampaignPause(errorCode))
                    {
                        logger.LogWarning(
                            "Meta returned policy code {Code} for campaign {CampaignId} — pausing campaign.",
                            errorCode, campaignId);

                        campaign.Status = CampaignStatus.Paused;
                        // The send did not happen — release the claim so a resume re-sends this recipient.
                        recipient.Status = RecipientStatus.Queued;
                        await db.SaveChangesAsync(ct);

                        await notifier.CreateAsync(
                            campaign.CompanyId, campaignId, null,
                            NotificationType.CampaignFailed,
                            "Campaign paused — spam signal detected",
                            $"Meta flagged this campaign for policy violations (code {errorCode}). " +
                            "Campaign paused. Review your template content and contact list quality before resuming.",
                            ct);
                        return;
                    }

                    // Transient throughput throttle (130429 / 131056): Meta asked us to slow down, so the send
                    // did NOT happen. Release the claim (Sending→Queued), persist progress, and reschedule the
                    // batch with backoff — the same graceful path as our own per-second limiter. Retrying the
                    // same Queued recipient is duplicate-safe; failing it would drop a deliverable message.
                    if (MetaPolicyErrorCodes.IsTransientThrottle(errorCode))
                    {
                        logger.LogWarning(
                            "Meta returned throttle code {Code} for campaign {CampaignId} — backing off and rescheduling batch.",
                            errorCode, campaignId);

                        recipient.Status = RecipientStatus.Queued;
                        await db.SaveChangesAsync(ct);
                        await RescheduleBatchAsync(
                            campaign, campaignId,
                            new RateLimitResult(false, TimeSpan.Zero, RateLimitReason.PerSecondThrottle),
                            db, notifier, ct, retryAfter);
                        return;
                    }

                    // Transient transport error (network blip / Meta 5xx / 408 / bare 429): the send didn't
                    // land, so keep the recipient Queued and reschedule the batch with backoff rather than
                    // permanently failing a deliverable message (H11). The atomic send-claim (C2) makes the
                    // reschedule duplicate-safe; honour any Retry-After Meta supplied.
                    if (MetaPolicyErrorCodes.IsTransientTransportError(errorCode))
                    {
                        logger.LogWarning(
                            "Transient send error {Code} for campaign {CampaignId} — recipient stays Queued, rescheduling batch.",
                            errorCode, campaignId);

                        recipient.Status = RecipientStatus.Queued;
                        await db.SaveChangesAsync(ct);
                        await RescheduleBatchAsync(
                            campaign, campaignId,
                            new RateLimitResult(false, TimeSpan.Zero, RateLimitReason.PerSecondThrottle),
                            db, notifier, ct, retryAfter);
                        return;
                    }

                    // Recipient isn't on WhatsApp (131026): stamp the contact invalid so every future
                    // campaign's registration gate skips it. The send genuinely failed for this run.
                    if (MetaPolicyErrorCodes.IsUndeliverableRecipient(errorCode)
                        && recipient.Contact.IsWhatsAppValid)
                    {
                        recipient.Contact.IsWhatsAppValid = false;
                        recipient.Contact.WhatsAppInvalidAt = DateTime.UtcNow;
                        logger.LogInformation(
                            "Contact {ContactId} marked not on WhatsApp (code {Code}) — will be skipped in future sends.",
                            recipient.ContactId, errorCode);
                    }

                    // Non-policy failure — mark recipient failed and continue the batch.
                    recipient.Status = RecipientStatus.Failed;
                    recipient.ErrorCode = errorCode ?? "SEND_FAILED";
                    logger.LogWarning(
                        "CampaignBatchSendJob: failed sending to contact {ContactId} in campaign {CampaignId}: {Error}",
                        recipient.ContactId, campaignId, errorCode);
                }
            }
            catch (Exception ex)
            {
                recipient.Status = RecipientStatus.Failed;
                recipient.ErrorCode = "JOB_ERROR";
                logger.LogError(ex,
                    "CampaignBatchSendJob: exception sending to contact {ContactId} in campaign {CampaignId}.",
                    recipient.ContactId, campaignId);
            }
            finally
            {
                // Reserve-then-send: any path that didn't commit the send as Sent returns the reservation,
                // exactly once (the success path sets sendSucceeded before this runs). Branches that leave the
                // recipient Queued for retry will re-reserve on the retry, so this nets to zero for them.
                if (!sendSucceeded)
                    await meter.RefundAsync(reservedSubId.Value, 1, ct);
            }

            // YELLOW pacing: space real sends out to lower the effective per-second rate for an at-risk
            // number (M5). Only after a committed send — skips/failures aren't penalised.
            if (sendSucceeded && isYellow)
                await Task.Delay(YellowInterSendDelay, ct);
        }

        await db.SaveChangesAsync(ct);

        // Check if more Queued rows remain — if so, chain the next batch.
        var remaining = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .CountAsync(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Queued, ct);

        if (remaining > 0 && campaign.Status == CampaignStatus.Running)
        {
            var nextJobId = jobClient.Enqueue<CampaignBatchSendJob>(
                j => j.RunAsync(campaignId, CancellationToken.None));
            campaign.HangfireJobId = nextJobId;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "CampaignBatchSendJob: campaign {Id} — {Remaining} remaining, chaining next batch (job {JobId}).",
                campaignId, remaining, nextJobId);
        }
        else if (campaign.Status == CampaignStatus.Running)
        {
            if (campaign.ScheduleType == ScheduleType.Recurring)
            {
                // Recurring: reset to Scheduled so the next Hangfire cron firing can re-snapshot and re-run.
                campaign.Status = CampaignStatus.Scheduled;
                campaign.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "CampaignBatchSendJob: recurring campaign {Id} batch complete — reset to Scheduled for next run.",
                    campaignId);
            }
            else
            {
                campaign.Status = CampaignStatus.Completed;
                campaign.CompletedAt = DateTime.UtcNow;
                campaign.HangfireJobId = null;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("CampaignBatchSendJob: campaign {Id} completed all recipients.", campaignId);
                await NotifyCompletedAsync(campaign, db, notifier, ct);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits the one-time "campaign completed" notification with a delivery summary. Idempotent: the
    /// unique index on (CampaignId, Type) means at most one CampaignCompleted per campaign, so it is safe
    /// to call from either completion path.
    /// </summary>
    private static async Task NotifyCompletedAsync(
        Campaign campaign, AppDbContext db, Notifications.INotificationService notifier, CancellationToken ct)
    {
        var counts = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Where(r => r.CampaignId == campaign.Id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sent = g.Count(r => r.Status == RecipientStatus.Accepted
                                    || r.Status == RecipientStatus.Sent
                                    || r.Status == RecipientStatus.Delivered
                                    || r.Status == RecipientStatus.Read),
                Failed = g.Count(r => r.Status == RecipientStatus.Failed),
                Skipped = g.Count(r => r.Status == RecipientStatus.Skipped),
                Total = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        var sent = counts?.Sent ?? 0;
        var failed = counts?.Failed ?? 0;
        var skipped = counts?.Skipped ?? 0;
        var total = counts?.Total ?? 0;

        var body = $"\"{campaign.Name}\" finished: {sent} sent"
                 + (failed > 0 ? $", {failed} failed" : string.Empty)
                 + (skipped > 0 ? $", {skipped} skipped" : string.Empty)
                 + $" of {total} recipients.";

        await notifier.CreateAsync(
            campaign.CompanyId, campaign.Id, null,
            Notifications.Entities.NotificationType.CampaignCompleted,
            "Campaign completed",
            body, ct);
    }

    /// <summary>
    /// Throttled mid-batch: persist progress and re-schedule the next batch instead of throwing. A
    /// per-second throttle backs off a few seconds with jitter (so concurrent campaigns don't wake in
    /// lockstep); a daily-tier exhaustion waits until the 24-hour window rolls over. Recipients stay Queued,
    /// so the rescheduled run resumes exactly where this one stopped — no double-send.
    /// </summary>
    /// <summary>Ceiling on a Meta-requested backoff so a pathological Retry-After can't park a campaign for
    /// hours; the daily-tier reschedule uses its own (longer) until-midnight delay and is unaffected.</summary>
    private static readonly TimeSpan MaxThrottleBackoff = TimeSpan.FromMinutes(10);

    private async Task RescheduleBatchAsync(
        Campaign campaign, Guid campaignId, RateLimitResult permit, AppDbContext db,
        Notifications.INotificationService notifier, CancellationToken ct, TimeSpan? retryAfterFloor = null)
    {
        var delay = permit.Reason == RateLimitReason.DailyTierExceeded
            ? DelayUntilNextUtcDay()
            : TimeSpan.FromSeconds(5 + Random.Shared.Next(0, 10));

        // Honour Meta's Retry-After when it asked for a longer cool-down than our default jitter: retrying
        // sooner than Meta wants just re-trips the throttle and digs the number deeper (M4). Capped so a bad
        // header value can't stall the campaign indefinitely.
        if (retryAfterFloor is { } floor && floor > delay)
            delay = floor < MaxThrottleBackoff ? floor : MaxThrottleBackoff;

        // A daily-tier pause lasts hours — tell the tenant. Idempotent (unique index on CampaignId+Type),
        // so repeated reschedules emit at most one notification per campaign.
        if (permit.Reason == RateLimitReason.DailyTierExceeded)
        {
            await notifier.CreateAsync(
                campaign.CompanyId, campaignId, null,
                Notifications.Entities.NotificationType.CampaignThrottled,
                "Campaign paused — daily limit reached",
                $"\"{campaign.Name}\" reached its WhatsApp number's daily messaging limit and will resume automatically after the daily reset.",
                ct);
        }

        var jobId = jobClient.Schedule<CampaignBatchSendJob>(
            j => j.RunAsync(campaignId, CancellationToken.None), delay);
        campaign.HangfireJobId = jobId;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "CampaignBatchSendJob: campaign {Id} throttled ({Reason}) — rescheduled in {DelaySeconds}s (job {JobId}).",
            campaignId, permit.Reason, (int)delay.TotalSeconds, jobId);
    }

    /// <summary>Delay from now until just after the next UTC midnight (when the daily tier counter resets).</summary>
    private static TimeSpan DelayUntilNextUtcDay()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1).AddMinutes(5) - now;
    }

    /// <summary>Parse VariableMapping JSON into a flat dictionary: "body_1" → "Name", etc.</summary>
    private static Dictionary<string, string> ResolveMapping(string variableMappingJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(variableMappingJson);
            foreach (var section in doc.RootElement.EnumerateObject())
            {
                foreach (var kv in section.Value.EnumerateObject())
                    result[$"{section.Name}_{kv.Name}"] = kv.Value.GetString() ?? string.Empty;
            }
        }
        catch { /* malformed mapping — return empty, will send without variables */ }
        return result;
    }

    /// <summary>
    /// Resolve contact attribute values from the mapping.
    /// Supports: "Name", "Phone", and "attributes.{key}" dot-paths.
    /// </summary>
    private static Dictionary<string, string> BuildResolvedVariables(
        Dictionary<string, string> mapping, Features.Contacts.Entities.Contact contact)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, attrPath) in mapping)
        {
            var value = attrPath switch
            {
                "Name" => contact.Name,
                "Phone" => contact.Phone,
                _ => string.Empty
            };
            result[key] = value;
        }
        return result;
    }

    /// <summary>Build a plain-text preview of the message body for the Message.Body column.</summary>
    private static string BuildBodyPreview(string? bodyText, Dictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(bodyText)) return string.Empty;
        var result = bodyText;
        foreach (var (key, value) in vars)
        {
            // key format: "body_1", replace {{1}} in the body text
            if (key.StartsWith("body_", StringComparison.OrdinalIgnoreCase))
            {
                var index = key["body_".Length..];
                result = result.Replace($"{{{{{index}}}}}", value);
            }
        }
        return result.Length > 4096 ? result[..4096] : result;
    }

    /// <summary>
    /// Upload the stored sample media bytes to the phone number's <c>/media</c> endpoint and return the
    /// resulting media id, to be referenced from a template message's media header. Returns null on
    /// failure (logged) so the caller can fail the affected recipients without throwing the whole batch.
    /// </summary>
    private async Task<string?> UploadHeaderMediaAsync(
        string phoneNumberId, string accessToken, TemplateMediaSample sample, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whatsapp"), "messaging_product");
        form.Add(new StringContent(sample.ContentType), "type");

        var fileContent = new ByteArrayContent(sample.Data);
        if (MediaTypeHeaderValue.TryParse(sample.ContentType, out var parsedType))
            fileContent.Headers.ContentType = parsedType;
        form.Add(fileContent, "file", sample.FileName ?? "header");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"v19.0/{phoneNumberId}/media") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Header media upload failed ({Status}) for phone {Phone}: {Json}",
                    (int)res.StatusCode, phoneNumberId, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error uploading header media for phone {Phone}.", phoneNumberId);
            return null;
        }
    }

    /// <summary>Call Meta's template message send endpoint. On a throttle response, <c>RetryAfter</c> carries
    /// Meta's requested cool-down (from the <c>Retry-After</c> header) when present, else null.</summary>
    private async Task<(string? ExternalId, string? ErrorCode, TimeSpan? RetryAfter)> SendTemplateMessageAsync(
        string phoneNumberId, string accessToken, string toPhone,
        string templateName, string language,
        Dictionary<string, string> resolvedVars,
        (string Type, string MediaId)? headerMedia,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("MetaGraph");

        // Build body and header parameter components from resolved variables.
        // Numeric sort ("body_2" < "body_10") instead of lexicographic.
        var bodyParams = resolvedVars
            .Where(kv => kv.Key.StartsWith("body_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => int.TryParse(kv.Key["body_".Length..], out var n) ? n : 0)
            .Select(kv => new { type = "text", text = kv.Value })
            .ToArray();

        var headerParams = resolvedVars
            .Where(kv => kv.Key.StartsWith("header_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => int.TryParse(kv.Key["header_".Length..], out var n) ? n : 0)
            .Select(kv => new { type = "text", text = kv.Value })
            .ToArray();

        var componentList = new List<object>();
        // A header is either media OR text params — never both. Media takes precedence.
        if (headerMedia is { } hm)
        {
            object mediaParam = hm.Type switch
            {
                "video" => new { type = "video", video = new { id = hm.MediaId } },
                "document" => new { type = "document", document = new { id = hm.MediaId } },
                _ => new { type = "image", image = new { id = hm.MediaId } },
            };
            componentList.Add(new { type = "header", parameters = new[] { mediaParam } });
        }
        else if (headerParams.Length > 0)
        {
            componentList.Add(new { type = "header", parameters = headerParams });
        }
        if (bodyParams.Length > 0)
            componentList.Add(new { type = "body", parameters = bodyParams });

        object components = componentList.Count > 0 ? componentList.ToArray() : Array.Empty<object>();

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language },
                components
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, $"v19.0/{phoneNumberId}/messages") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var msgId = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
                return (msgId, null, null);
            }

            var errorCode = ParseMetaErrorCode(json);

            // Meta's suggested cool-down, if it sent one (Retry-After as a delta or an absolute date).
            var retryAfter = res.Headers.RetryAfter?.Delta
                ?? (res.Headers.RetryAfter?.Date is { } when ? when - DateTimeOffset.UtcNow : (TimeSpan?)null);

            logger.LogWarning("Meta API {Status} for {Phone}: {Json}", (int)res.StatusCode, toPhone, json);
            return (null, errorCode ?? $"HTTP_{(int)res.StatusCode}", retryAfter);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Meta Graph API for {Phone}.", toPhone);
            return (null, "NETWORK_ERROR", null);
        }
    }

    /// <summary>
    /// Extracts Meta's <c>error.code</c> from a Graph API error body as a string the
    /// <see cref="MetaPolicyErrorCodes"/> classifiers can match. Meta returns the code as a JSON NUMBER
    /// (e.g. <c>131048</c>); a prior <c>GetString()</c> threw on that shape and left the code null, which
    /// silently disabled every policy response (spam pause, account restriction, transient throttle,
    /// undeliverable). Handles both Number and String shapes and returns null on any unexpected body.
    /// </summary>
    public static string? ParseMetaErrorCode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("code", out var code))
            {
                return code.ValueKind == JsonValueKind.Number
                    ? code.GetInt32().ToString(CultureInfo.InvariantCulture)
                    : code.GetString();
            }
        }
        catch { /* not JSON or unexpected shape — fall through to null */ }
        return null;
    }
}
