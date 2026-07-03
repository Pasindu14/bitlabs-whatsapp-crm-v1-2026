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

/// <summary>
/// Pre-send breakdown of a campaign's target audience, mirroring the send-time skip precedence
/// (no-consent → opted-out → not-on-WhatsApp). Buckets are mutually exclusive and sum to Total, so the
/// operator sees exactly how many messages will actually go out before launching.
/// </summary>
public record CampaignAudienceHealthResponse(
    int Total,
    /// <summary>Will actually be sent given the campaign's consent-override setting.</summary>
    int Sendable,
    /// <summary>Skipped: no recorded opt-in (0 when the consent override is on).</summary>
    int NoConsent,
    /// <summary>Skipped: opted out (STOP) — a hard suppression the override never bypasses.</summary>
    int OptedOut,
    /// <summary>Skipped: a prior send proved the number isn't on WhatsApp (131026).</summary>
    int Invalid,
    /// <summary>Subset of Sendable that has no recorded consent but will send because the override is on.</summary>
    int NoConsentOverridden,
    /// <summary>Whether the campaign's consent override is enabled.</summary>
    bool ConsentOverride
);
