namespace wa_api.Features.Analytics.DTOs;

public record CampaignPerformanceRow(
    Guid Id,
    string Name,
    string Status,
    DateTime? LaunchedAt,
    int TotalRecipients,
    int Sent,
    int Delivered,
    int Read,
    int Failed,
    int Skipped
);

public record CampaignPerformanceResponse(
    IReadOnlyList<CampaignPerformanceRow> Campaigns
);
