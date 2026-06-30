namespace wa_api.Features.Admin.Logging;

public record LogEntry(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Exception
);
