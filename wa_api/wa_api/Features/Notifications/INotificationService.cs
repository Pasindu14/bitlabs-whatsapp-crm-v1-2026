using wa_api.Features.Notifications.Entities;

namespace wa_api.Features.Notifications;

public interface INotificationService
{
    Task CreateAsync(Guid companyId, Guid? campaignId, Guid? templateId, NotificationType type,
                     string title, string body, CancellationToken ct = default);

    Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid companyId, CancellationToken ct = default);
}
