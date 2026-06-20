using Microsoft.EntityFrameworkCore;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Infrastructure.Jobs;

/// <summary>
/// Polls Meta for every PENDING template and syncs its approval verdict. Runs cross-tenant —
/// bypasses the EF query filter intentionally (Plan 004 Step 3). The proper push mechanism is the
/// <c>message_template_status_update</c> webhook (PRD Phase 5.2); this poll is the interim + safety net.
/// </summary>
public class TemplateStatusSyncJob(IServiceScopeFactory scopeFactory, ILogger<TemplateStatusSyncJob> logger)
{
    public async Task RunAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meta = scope.ServiceProvider.GetRequiredService<IMetaTemplateClient>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Only non-terminal templates need polling; include the connection for its access token.
        var pending = await db.Templates
            .IgnoreQueryFilters()
            .Include(t => t.WabaConnection)
            .Where(t => t.IsActive && t.Status == TemplateStatus.Pending && t.MetaTemplateId != null)
            .ToListAsync();

        if (pending.Count == 0)
        {
            logger.LogInformation("TemplateStatusSync: no pending templates to poll.");
            return;
        }

        logger.LogInformation("TemplateStatusSync: polling {Count} pending template(s).", pending.Count);
        var now = DateTime.UtcNow;
        var newlyApproved = new List<Template>();

        foreach (var t in pending)
        {
            if (t.WabaConnection is null || string.IsNullOrEmpty(t.MetaTemplateId))
                continue;

            var status = await meta.GetStatusAsync(t.MetaTemplateId, t.WabaConnection.EncryptedAccessToken);
            if (status is null)
                continue;   // transient/auth error — retry next cycle

            var prevStatus = t.Status;
            TemplateStatusMapper.Apply(t, status, now);
            if (t.Status != TemplateStatus.Pending)
                logger.LogInformation("TemplateStatusSync: {Name} ({Lang}) → {Status}", t.Name, t.Language, t.Status);
            if (prevStatus != TemplateStatus.Approved && t.Status == TemplateStatus.Approved)
                newlyApproved.Add(t);
        }

        await db.SaveChangesAsync();

        foreach (var t in newlyApproved)
            await notifier.CreateAsync(
                t.CompanyId, null, t.Id,
                NotificationType.TemplateApproved,
                $"Template \"{t.Name}\" has been approved",
                "Your WhatsApp template is ready to use in campaigns.");

        logger.LogInformation("TemplateStatusSync: finished at {Time:o}.", now);
    }
}
