namespace wa_api.Common.Errors;

public static class ResponseHelper
{
    public static ApiResponse<T> Ok<T>(T data, string? traceId = null)
        => new(true, data, null, traceId);

    public static ApiResponse<T> Created<T>(T data, string? traceId = null)
        => new(true, data, null, traceId);

    public static ApiResponse<T> Paged<T>(
        T data, int page, int pageSize, int total, string? traceId = null)
    {
        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        return new(true, data, new PaginationMeta(page, pageSize, total, totalPages), traceId);
    }
}
