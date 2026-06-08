using Microsoft.EntityFrameworkCore;
using Npgsql;
using wa_api.Common.Errors;
using System.Text.Json;

namespace wa_api.Common.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex) { await HandleExceptionAsync(context, ex); }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString()
                            ?? Guid.NewGuid().ToString();

        var (statusCode, error) = exception switch
        {
            ValidationException vex => (400, new ApiError(
                vex.ErrorCode, vex.Message, null,
                vex.Fields, null, correlationId, DateTime.UtcNow)),

            TokenExpiredException tex => (401, new ApiError(
                tex.ErrorCode, tex.Message, null,
                null, null, correlationId, DateTime.UtcNow)),

            AuthenticationException aex => (401, new ApiError(
                aex.ErrorCode, aex.Message, null,
                null, null, correlationId, DateTime.UtcNow)),

            AuthorizationException forEx => (403, new ApiError(
                forEx.ErrorCode, forEx.Message, null,
                null, null, correlationId, DateTime.UtcNow)),

            NotFoundException nex => (404, new ApiError(
                nex.ErrorCode, nex.Message, null,
                null, null, correlationId, DateTime.UtcNow)),

            DbUpdateException dbEx when dbEx.InnerException is PostgresException pgEx && pgEx.SqlState == "23505"
                => (409, new ApiError(
                    "DUPLICATE_RESOURCE", "A record with the same unique value already exists.", null,
                    null, null, correlationId, DateTime.UtcNow)),

            ConcurrencyConflictException cex => (409, new ApiError(
                cex.ErrorCode, cex.Message,
                "Review the latest version before resubmitting.",
                null, cex.Data, correlationId, DateTime.UtcNow)),

            ConflictException confEx => (409, new ApiError(
                confEx.ErrorCode, confEx.Message, null,
                null, confEx.Data, correlationId, DateTime.UtcNow)),

            BusinessRuleException bex => (422, new ApiError(
                bex.ErrorCode, bex.Message, null,
                null, bex.Data, correlationId, DateTime.UtcNow)),

            RateLimitException => (429, new ApiError(
                "RATE_LIMITED", "Too many requests.",
                "Retry after the indicated time.",
                null, null, correlationId, DateTime.UtcNow)),

            InfrastructureException iex => (503, new ApiError(
                iex.ErrorCode, iex.Message, null,
                null, null, correlationId, DateTime.UtcNow)),

            _ => (500, new ApiError(
                "INTERNAL_ERROR", "An unexpected error occurred.", null,
                null, null, correlationId, DateTime.UtcNow))
        };

        if (statusCode >= 500)
            _logger.LogError(exception,
                "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);
        else
            _logger.LogWarning(exception,
                "Handled exception {Code}. CorrelationId: {CorrelationId}",
                error.Code, correlationId);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new ApiErrorResponse(false, error), _jsonOptions));
    }
}
