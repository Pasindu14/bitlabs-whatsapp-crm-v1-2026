namespace wa_api.Features.Notifications.Dtos;

public record NotificationResponse(
    Guid Id,
    Guid? CampaignId,
    Guid? TemplateId,
    string Type,
    string Title,
    string Body,
    bool IsRead,
    DateTime CreatedAt
);

public record NotificationListResponse(
    IReadOnlyList<NotificationResponse> Items,
    int UnreadCount,
    int TotalCount
);
