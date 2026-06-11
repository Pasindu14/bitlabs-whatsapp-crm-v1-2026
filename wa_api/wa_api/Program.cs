using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using wa_api.Common.Audit;
using wa_api.Common.Errors;
using wa_api.Common.Extensions;
using wa_api.Common.Middleware;
using wa_api.Infrastructure.Caching;
using wa_api.Infrastructure.Jobs;
using wa_api.Infrastructure.Locking;
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

    // Per-request tenant identity (resolved from JWT claims). Scoped so it can be
    // injected into AppDbContext for the global query filter + CompanyId auto-stamp.
    builder.Services.AddScoped<wa_api.Common.Tenancy.ITenantContext, wa_api.Common.Tenancy.HttpTenantContext>();

    var baseConnStr = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
    // The app-side Npgsql pool sits IN FRONT OF the Supavisor pooler, so its Max Pool
    // Size must stay UNDER the pooler's client cap (session mode / port 5432 = 15 clients),
    // shared across every app instance. Keep this small per instance.
    // NOTE: For production scale, switch the connection string to Supavisor TRANSACTION
    // mode (port 6543), which allows far more concurrent clients.
    var pooledConnStr = baseConnStr.TrimEnd(';')
        + ";Maximum Pool Size=10;Minimum Pool Size=2;Connection Idle Lifetime=300";

    // AddDbContext (not pool): a pooled context can't take the scoped ITenantContext the
    // global query filter needs. This is DbContext object-pooling only — the Npgsql
    // CONNECTION pool sizing above (Maximum Pool Size) is in the connection string and
    // is unaffected, so the Supavisor client cap still holds.
    builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
        opt.UseNpgsql(
               pooledConnStr,
               npgsql => npgsql
                   .CommandTimeout(30)
                   .EnableRetryOnFailure(3))
           .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

    // ── Caching + Locking (Redis when configured, else memory / Postgres) ──
    var redisConnection = builder.Configuration["REDIS_CONNECTION"];
    if (!string.IsNullOrWhiteSpace(redisConnection))
    {
        builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnection));
        builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
    }
    else
    {
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSingleton<IDistributedLockService, PostgresAdvisoryLockService>();
    }
    builder.Services.AddScoped<ICacheService, DistributedCacheService>();

    // ── Idempotency ────────────────────────────────────────────────────────
    builder.Services.AddScoped<IIdempotencyService, PostgresIdempotencyService>();
    builder.Services.AddHostedService<IdempotencyCleanupService>();

    // ── Background jobs (Hangfire) ─────────────────────────────────────────
    builder.Services.AddPlatformHangfire(builder.Configuration);
    builder.Services.AddTransient<WabaHealthCheckJob>();
    builder.Services.AddTransient<SubscriptionPeriodResetJob>();
    builder.Services.AddTransient<TemplateStatusSyncJob>();
    builder.Services.AddTransient<wa_api.Features.Webhooks.Processing.WebhookProcessingJob>();
    builder.Services.AddTransient<wa_api.Features.Webhooks.Processing.WebhookSweeperJob>();

    // Named HttpClient for Meta Graph API calls (base address set here; auth header per-request).
    builder.Services.AddHttpClient("MetaGraph", c =>
    {
        c.BaseAddress = new Uri("https://graph.facebook.com/");
        c.Timeout = TimeSpan.FromSeconds(15);
    });

    // ── Authentication & Authorization (JWT bearer) ───────────────────────
    builder.Services.AddPlatformAuthentication(builder.Configuration);

    // ── Feature services ───────────────────────────────────────────────────
    builder.Services.AddScoped<wa_api.Features.Companies.ICompanyService, wa_api.Features.Companies.CompanyService>();
    builder.Services.AddScoped<wa_api.Features.WhatsApp.IWabaConnectionService, wa_api.Features.WhatsApp.WabaConnectionService>();
    builder.Services.AddScoped<wa_api.Features.WhatsApp.IMetaCredentialValidator, wa_api.Features.WhatsApp.MetaCredentialValidator>();
    builder.Services.AddScoped<wa_api.Features.Users.IUserService, wa_api.Features.Users.UserService>();
    builder.Services.AddScoped<wa_api.Features.Contacts.IContactService, wa_api.Features.Contacts.ContactService>();
    builder.Services.AddScoped<wa_api.Features.ContactLists.IContactListService, wa_api.Features.ContactLists.ContactListService>();
    builder.Services.AddScoped<wa_api.Infrastructure.RateLimiting.IWabaRateLimiter, wa_api.Infrastructure.RateLimiting.FixedWindowWabaRateLimiter>();
    builder.Services.AddScoped<wa_api.Features.Messages.IMessageService, wa_api.Features.Messages.MessageService>();
    builder.Services.AddScoped<wa_api.Features.Plans.IPlanService, wa_api.Features.Plans.PlanService>();
    builder.Services.AddScoped<wa_api.Features.Subscriptions.ISubscriptionService, wa_api.Features.Subscriptions.SubscriptionService>();
    builder.Services.AddScoped<wa_api.Features.Templates.ITemplateService, wa_api.Features.Templates.TemplateService>();
    builder.Services.AddScoped<wa_api.Features.Templates.IMetaTemplateClient, wa_api.Features.Templates.MetaTemplateClient>();
    builder.Services.AddScoped<wa_api.Features.Templates.IMetaMediaUploader, wa_api.Features.Templates.MetaMediaUploader>();
    builder.Services.AddScoped<wa_api.Common.Subscriptions.ISubscriptionGate, wa_api.Common.Subscriptions.SubscriptionGate>();

    // ── Webhooks (Meta inbound: template status 5.2 + delivery status 6.4; inbound stub → Phase 7) ──
    builder.Services.AddScoped<wa_api.Features.Webhooks.Signature.IMetaSignatureVerifier, wa_api.Features.Webhooks.Signature.MetaSignatureVerifier>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Ingestion.IWebhookInboxService, wa_api.Features.Webhooks.Ingestion.WebhookInboxService>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Processing.IWebhookDispatcher, wa_api.Features.Webhooks.Processing.WebhookDispatcher>();
    // All handlers share one interface so the dispatcher receives them via IEnumerable<IWebhookEventHandler>.
    builder.Services.AddScoped<wa_api.Features.Webhooks.Handlers.IWebhookEventHandler, wa_api.Features.Webhooks.Handlers.TemplateStatusWebhookHandler>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Handlers.IWebhookEventHandler, wa_api.Features.Webhooks.Handlers.MessageStatusWebhookHandler>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Handlers.IWebhookEventHandler, wa_api.Features.Webhooks.Handlers.InboundMessageWebhookHandler>();
    // Phase 7 seam: register the SignalR hub here and swap InboundMessageWebhookHandler's body for an
    // IHubContext push + 24h-window Conversation update — the receiver, dispatcher, and queue stay unchanged.

    // ── Observability ──────────────────────────────────────────────────────
    builder.Services.AddPlatformHealthChecks(builder.Configuration);
    var appInsightsConn = builder.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(appInsightsConn))
    {
        builder.Services.AddApplicationInsightsTelemetry(o => o.ConnectionString = appInsightsConn);
    }

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
    builder.Services.AddSwaggerGen(c =>
    {
        var scheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Paste the JWT from /auth/login (no 'Bearer ' prefix needed).",
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
            {
                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        };
        c.AddSecurityDefinition("Bearer", scheme);
        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            [scheme] = Array.Empty<string>()
        });
    });

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
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapPlatformHealthChecks();

    // ── Hangfire dashboard + recurring jobs ────────────────────────────────
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter(app.Environment)]
    });
    RecurringJob.AddOrUpdate<HeartbeatJob>("heartbeat", j => j.Run(), Cron.Hourly);
    RecurringJob.AddOrUpdate<WabaHealthCheckJob>("waba-health-check", j => j.RunAsync(), Cron.MinuteInterval(15));
    RecurringJob.AddOrUpdate<SubscriptionPeriodResetJob>("subscription-period-reset", j => j.RunAsync(), Cron.Daily);
    RecurringJob.AddOrUpdate<TemplateStatusSyncJob>("template-status-sync", j => j.RunAsync(), Cron.MinuteInterval(15));
    RecurringJob.AddOrUpdate<wa_api.Features.Webhooks.Processing.WebhookSweeperJob>("webhook-sweeper", j => j.RunAsync(), Cron.MinuteInterval(5));

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
