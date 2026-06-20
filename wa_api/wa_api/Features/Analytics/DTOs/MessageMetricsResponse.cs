namespace wa_api.Features.Analytics.DTOs;

public record DailyMessageCount(string Date, int Sent, int Delivered, int Read, int Failed);

public record MessageMetricsResponse(
    int TotalSent,
    int TotalDelivered,
    int TotalRead,
    int TotalFailed,
    IReadOnlyList<DailyMessageCount> Daily
);
