namespace wa_api.Features.Monitoring.Dtos;

/// <summary>Health snapshot for one company over the last 30 days.</summary>
public record CompanyHealthDto(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    int TotalSent30d,
    int Failed30d,
    int Billable30d,
    double FailureRate,
    bool IsFlagged,
    int ActiveCampaigns,
    DateTime? LastMessageAt,
    /// <summary>Worst Meta quality rating (GREEN/YELLOW/RED) across the company's WABA numbers; null if unsynced.</summary>
    string? QualityRating
);

public record MonitoringResponse(IReadOnlyList<CompanyHealthDto> Companies);
