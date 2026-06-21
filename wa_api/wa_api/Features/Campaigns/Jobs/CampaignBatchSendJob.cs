using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Messages.Entities;
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
    private readonly RateLimitOptions _rateOptions = rateOptions.Value;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task RunAsync(Guid campaignId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rateLimiter = scope.ServiceProvider.GetRequiredService<IWabaRateLimiter>();
        var notifier = scope.ServiceProvider.GetRequiredService<Notifications.INotificationService>();

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

        var waba = campaign.Template.WabaConnection;

        // The number's 24-hour unique-recipient tier cap (campaign sends are business-initiated).
        var tierLimit = (int)waba.MessagingTier;

        // Load one batch of Queued recipients with their contacts.
        var batch = await db.CampaignRecipients
            .IgnoreQueryFilters()
            .Include(r => r.Contact)
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .Take(BatchSize)
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
            return;
        }

        var resolvedMapping = ResolveMapping(campaign.VariableMapping);

        foreach (var recipient in batch)
        {
            // Re-check campaign status inside loop — pause/cancel can arrive mid-batch.
            if (campaign.Status is CampaignStatus.Paused or CampaignStatus.Cancelled)
                break;

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

            try
            {
                var resolvedVars = BuildResolvedVariables(resolvedMapping, recipient.Contact);
                var (externalId, errorCode) = await SendTemplateMessageAsync(
                    waba.PhoneNumberId, waba.EncryptedAccessToken,
                    recipient.Contact.Phone,
                    campaign.Template.Name, campaign.Template.Language,
                    resolvedVars, ct);

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
                        Status = MessageStatus.Sent,
                        ExternalMessageId = externalId,
                    };
                    db.Messages.Add(message);
                    await db.SaveChangesAsync(ct);

                    recipient.Status = RecipientStatus.Sent;
                    recipient.MessageId = message.Id;
                    recipient.ResolvedVariables = JsonSerializer.Serialize(resolvedVars, JsonOpts);
                    campaign.SentCount++;
                }
                else
                {
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
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Throttled mid-batch: persist progress and re-schedule the next batch instead of throwing. A
    /// per-second throttle backs off a few seconds with jitter (so concurrent campaigns don't wake in
    /// lockstep); a daily-tier exhaustion waits until the 24-hour window rolls over. Recipients stay Queued,
    /// so the rescheduled run resumes exactly where this one stopped — no double-send.
    /// </summary>
    private async Task RescheduleBatchAsync(
        Campaign campaign, Guid campaignId, RateLimitResult permit, AppDbContext db,
        Notifications.INotificationService notifier, CancellationToken ct)
    {
        var delay = permit.Reason == RateLimitReason.DailyTierExceeded
            ? DelayUntilNextUtcDay()
            : TimeSpan.FromSeconds(5 + Random.Shared.Next(0, 10));

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

    /// <summary>Call Meta's template message send endpoint.</summary>
    private async Task<(string? ExternalId, string? ErrorCode)> SendTemplateMessageAsync(
        string phoneNumberId, string accessToken, string toPhone,
        string templateName, string language,
        Dictionary<string, string> resolvedVars,
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
        if (headerParams.Length > 0)
            componentList.Add(new { type = "header", parameters = headerParams });
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
                return (msgId, null);
            }

            string? errorCode = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                errorCode = doc.RootElement.GetProperty("error").GetProperty("code").GetString();
            }
            catch { /* ignore parse failures */ }

            logger.LogWarning("Meta API {Status} for {Phone}: {Json}", (int)res.StatusCode, toPhone, json);
            return (null, errorCode ?? $"HTTP_{(int)res.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error calling Meta Graph API for {Phone}.", toPhone);
            return (null, "NETWORK_ERROR");
        }
    }
}
