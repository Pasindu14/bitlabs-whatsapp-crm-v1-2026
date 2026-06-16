using wa_api.Features.Campaigns.Entities;

namespace wa_api.Features.Campaigns.Dtos;

public record CampaignResponse(
    Guid Id,
    string Name,
    Guid TemplateId,
    string TemplateName,
    /// <summary>All targeted contact lists (replaces the old single ContactListId).</summary>
    List<Guid> ContactListIds,
    List<string> ContactListNames,
    /// <summary>Individually targeted contacts (empty when targeting full lists).</summary>
    List<Guid> ContactIds,
    string VariableMapping,
    CampaignStatus Status,
    ScheduleType ScheduleType,
    DateTime? ScheduledAt,
    string? RecurrenceCron,
    int TotalRecipients,
    int SentCount,
    DateTime? LaunchedAt,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CampaignStatsResponse(
    Guid CampaignId,
    int TotalRecipients,
    int Queued,
    int Sent,
    int Delivered,
    int Read,
    int Failed,
    int Skipped,
    decimal DeliveryRate,
    decimal ReadRate
);

public record CampaignRecipientResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    string ContactPhone,
    RecipientStatus Status,
    string? ErrorCode,
    Guid? MessageId,
    DateTime CreatedAt
);
