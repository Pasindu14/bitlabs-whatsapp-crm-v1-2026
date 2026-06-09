using System.Globalization;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Templates;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Webhooks.Handlers;

/// <summary>
/// PRD 5.2 — applies a <c>message_template_status_update</c> to its Template in real time, reusing
/// <see cref="TemplateStatusMapper.Apply"/> (the exact mapping the 15-min poll uses). Routed by
/// MetaTemplateId, which already carries CompanyId, so no connection/phone_number_id is needed.
/// </summary>
public sealed class TemplateStatusWebhookHandler(AppDbContext db, ILogger<TemplateStatusWebhookHandler> logger)
    : IWebhookEventHandler
{
    public bool CanHandle(string field, WebhookChange change)
        => field == "message_template_status_update";

    public async Task HandleAsync(WebhookContext ctx, CancellationToken ct = default)
    {
        var v = ctx.Change.Value;
        if (v?.MessageTemplateId is not { } metaId)
        {
            logger.LogWarning("Template status webhook missing message_template_id — skipping.");
            return;
        }

        var metaTemplateId = metaId.ToString(CultureInfo.InvariantCulture);
        var template = await db.Templates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.MetaTemplateId == metaTemplateId, ct);
        if (template is null)
        {
            logger.LogInformation("Template status webhook for unknown MetaTemplateId {Id} — ignored.", metaTemplateId);
            return;
        }

        // Mapper maps event→status, keeps the rejection reason only when Rejected, and stamps LastSyncedAt.
        // Category isn't used by Apply, so null is fine. Unrecognized events (e.g. PENDING_DELETION) → no change.
        TemplateStatusMapper.Apply(template, new MetaTemplateStatus(v.Event, null, v.Reason), DateTime.UtcNow);
        logger.LogInformation("Template {Name} ({Lang}) → {Status} via webhook.",
            template.Name, template.Language, template.Status);
    }
}
