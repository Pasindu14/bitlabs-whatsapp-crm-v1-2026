using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Notifications.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Notifications;

public sealed class NotificationService(
    AppDbContext db,
    IHubContext<ChatHub> hub,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task CreateAsync(Guid companyId, Guid? campaignId, Guid? templateId, NotificationType type,
                                  string title, string body, CancellationToken ct = default)
    {
        var notification = new Entities.Notification
        {
            CompanyId = companyId,
            CampaignId = campaignId,
            TemplateId = templateId,
            Type = type,
            Title = title,
            Body = body,
            IsRead = false,
        };

        db.Notifications.Add(notification);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("UX_Notifications_CampaignId_Type") == true ||
            ex.InnerException?.Message.Contains("UX_Notifications_TemplateId_Type") == true)
        {
            // Duplicate notification for this campaign/template + type — silently discard.
            db.Entry(notification).State = EntityState.Detached;
            logger.LogDebug("Notification {Type} for campaign {CampaignId} / template {TemplateId} already exists — skipped.", type, campaignId, templateId);
            return;
        }

        // Push real-time event on the existing ChatHub company group.
        await hub.Clients
            .Group(ChatHub.GroupFor(companyId))
            .SendAsync("notification", new
            {
                id = notification.Id,
                type = type.ToString(),
                title,
                body,
                createdAt = notification.CreatedAt,
                isRead = false,
            }, ct);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        await db.Notifications
            .Where(n => n.Id == notificationId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task MarkAllReadAsync(Guid companyId, CancellationToken ct = default)
    {
        await db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.CompanyId == companyId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }
}
