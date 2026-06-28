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
    /// <summary>Whether this campaign bypasses the opt-in consent gate.</summary>
    bool OverrideConsentGate,
    /// <summary>Count of messages sent to contacts without recorded opt-in (audit).</summary>
    int NoConsentSentCount,
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
    /// <summary>Messages sent to contacts without recorded opt-in (consent override). Audit metric.</summary>
    int SentWithoutConsent,
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
    /// <summary>True when sent despite no recorded opt-in (campaign consent override was on).</summary>
    bool SentWithoutConsent,
    Guid? MessageId,
    DateTime CreatedAt
);
