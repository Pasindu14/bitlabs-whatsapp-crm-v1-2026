using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using wa_api.Common.Audit;
using wa_api.Common.Errors;
using wa_api.Common.Extensions;
using wa_api.Common.Middleware;
using wa_api.Infrastructure.Persistence;

// Bootstrap logger — captures failures during startup before the host is built.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Logging ───────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ── Database (Supabase Postgres via Supavisor pooler) ──────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<AuditInterceptor>();

    var baseConnStr = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
    // The app-side Npgsql pool sits IN FRONT OF the Supavisor pooler, so its Max Pool
    // Size must stay UNDER the pooler's client cap (session mode / port 5432 = 15 clients),
    // shared across every app instance. Keep this small per instance.
    // NOTE: For production scale, switch the connection string to Supavisor TRANSACTION
    // mode (port 6543), which allows far more concurrent clients.
    var pooledConnStr = baseConnStr.TrimEnd(';')
        + ";Maximum Pool Size=10;Minimum Pool Size=2;Connection Idle Lifetime=300";

    builder.Services.AddDbContextPool<AppDbContext>((sp, opt) =>
        opt.UseNpgsql(
               pooledConnStr,
               npgsql => npgsql
                   .CommandTimeout(30)
                   .EnableRetryOnFailure(3))
           .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

    // ── HTTP & API ────────────────────────────────────────────────────────
    builder.Services.AddControllers(options =>
        {
            // Every controller is served under /api/v1 (Section 2.1 — /api/v1 versioning).
            options.Conventions.Add(new RoutePrefixConvention("api/v1"));
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        })
        .ConfigureApiBehaviorOptions(options =>
        {
            // Model-binding/validation failures return the standard ApiError envelope.
            options.InvalidModelStateResponseFactory = ctx =>
            {
                var fields = ctx.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors
                            .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                            .ToArray());

                var correlationId = ctx.HttpContext.Items["CorrelationId"]?.ToString() ?? string.Empty;
                var error = new ApiError(
                    "VALIDATION_FAILED",
                    "One or more validation errors occurred.",
                    null, fields, null, correlationId, DateTime.UtcNow);

                return new BadRequestObjectResult(new ApiErrorResponse(false, error));
            };
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // ── Migrate & seed (dev/staging only) ─────────────────────────────────
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        await DataSeeder.SeedAsync(app.Services, app.Logger);
    }

    // ── Middleware Pipeline (ORDER MATTERS) ───────────────────────────────
    app.UseMiddleware<GlobalExceptionMiddleware>();  // 1. Catch all exceptions → ApiError
    app.UseMiddleware<CorrelationIdMiddleware>();    // 2. Correlation ID (sets context.Items)
    app.UseSerilogRequestLogging();                  // 3. Log every request with correlation id

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WhatsApp API v1"));
    }

    app.UseHttpsRedirection();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program accessible to WebApplicationFactory in integration tests (Phase 0 test setup).
public partial class Program { }
