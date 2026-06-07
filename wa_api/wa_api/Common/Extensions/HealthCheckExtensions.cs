using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace wa_api.Common.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddPlatformHealthChecks(
        this IServiceCollection services, IConfiguration config)
    {
        var healthChecks = services.AddHealthChecks();

        var connectionString = config.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
            healthChecks.AddNpgSql(connectionString, name: "postgresql", tags: ["ready"]);

        var redis = config["REDIS_CONNECTION"];
        if (!string.IsNullOrWhiteSpace(redis))
            healthChecks.AddRedis(redis, name: "redis", tags: ["ready"]);

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformHealthChecks(this IEndpointRouteBuilder app)
    {
        // Full report (all checks) with a JSON body.
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteJsonAsync });

        // Liveness — process is up; runs no dependency checks.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

        // Readiness — dependencies (DB, Redis) reachable.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = hc => hc.Tags.Contains("ready"),
            ResponseWriter = WriteJsonAsync
        });

        return app;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            traceId = context.Items["CorrelationId"]?.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message
            })
        };
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
