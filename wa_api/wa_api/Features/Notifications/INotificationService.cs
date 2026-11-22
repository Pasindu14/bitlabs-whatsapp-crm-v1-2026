using wa_api.Features.Notifications.Entities;

namespace wa_api.Features.Notifications;

public interface INotificationService
{
    Task CreateAsync(Guid companyId, Guid? campaignId, Guid? templateId, NotificationType type,
                     string title, string body, CancellationToken ct = default);

    /// <summary>A notification tied to neither a campaign nor a template (chatbot handoff, clinic CRM sync).</summary>
    Task CreateAsync(Guid companyId, NotificationType type, string title, string body,
                     CancellationToken ct = default)
        => CreateAsync(companyId, campaignId: null, templateId: null, type, title, body, ct);

    Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid companyId, CancellationToken ct = default);
}
