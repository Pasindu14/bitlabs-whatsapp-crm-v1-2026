namespace wa_api.Common.Errors;

public record ApiResponse<T>(
    bool Success,
    T? Data,
    PaginationMeta? Pagination,
    string? TraceId
);

public record PaginationMeta(
    int Page,
    int PageSize,
    int Total,
    int TotalPages
);

public record ApiErrorResponse(
    bool Success,
    ApiError Error
);

public record ApiError(
    string Code,
    string Message,
    string? Detail,
    Dictionary<string, string[]>? Fields,
    object? CurrentData,
    string? TraceId,
    DateTime Timestamp
);
