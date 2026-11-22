using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using wa_api.Features.Admin.Logging;
using StackExchange.Redis;
using wa_api.Common.Audit;
using wa_api.Common.Errors;
using wa_api.Common.Extensions;
using wa_api.Common.Middleware;
using wa_api.Infrastructure.Caching;
using wa_api.Infrastructure.Jobs;
using wa_api.Infrastructure.Locking;
using wa_api.Infrastructure.Persistence;
using wa_api.Infrastructure.Security;

// Bootstrap logger — captures failures during startup before the host is built.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Logging ───────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Sink(new InMemoryLogSink(InMemoryLogBuffer.Instance)));

    builder.Services.AddSingleton(InMemoryLogBuffer.Instance);

    // ── Request limits (M8) ───────────────────────────────────────────────
    // WhatsApp template media (video/docs) can approach ~100 MB; the framework default of 30 MB would 413
    // before the handler even runs. Set an explicit, intentional ceiling for both the raw body and multipart.
    const long MaxRequestBytes = 100L * 1024 * 1024;
    builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxRequestBytes);
    builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxRequestBytes);

    // ── Forwarded headers (M7) ─────────────────────────────────────────────
    // Kestrel runs behind Caddy (TLS terminates there), so without this every RemoteIpAddress is Caddy's
    // container IP and Request.Scheme is always http — corrupting audit logs and making UseHttpsRedirection a
    // no-op. Trust the single in-network proxy: the app port is only reachable through Caddy, so clearing the
    // known-proxy allowlist (which otherwise can't match Docker's dynamic bridge IP) is safe here.
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });

    // ── Database (Supabase Postgres via Supavisor pooler) ──────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<AuditInterceptor>();

    // Per-request tenant identity (resolved from JWT claims). Scoped so it can be
    // injected into AppDbContext for the global query filter + CompanyId auto-stamp.
    // Registered as itself as well as the interface: a background job resolves the concrete type to
    // call EnterCompany, while everything else takes the interface and cannot reassign the tenant.
    builder.Services.AddScoped<wa_api.Common.Tenancy.ScopedTenantContext>();
    builder.Services.AddScoped<wa_api.Common.Tenancy.ITenantContext>(
        sp => sp.GetRequiredService<wa_api.Common.Tenancy.ScopedTenantContext>());

    // ── At-rest secret encryption (H3) ─────────────────────────────────────
    // Encrypts WABA access tokens + per-connection app secrets at the DB boundary (AppDbContext value
    // converter). A valid base64 32-byte Encryption:Key enables AES-256-GCM; a missing key or the public
    // all-zero placeholder falls back to a passthrough protector with a loud warning (dev / key not yet
    // provisioned) rather than failing startup — reads/writes still work, just unencrypted.
    ITokenProtector tokenProtector = NullTokenProtector.Instance;
    var encryptionKeyRaw = builder.Configuration["Encryption:Key"];
    byte[]? encKey = null;
    if (!string.IsNullOrWhiteSpace(encryptionKeyRaw))
    {
        try { encKey = Convert.FromBase64String(encryptionKeyRaw); }
        catch (FormatException) { encKey = null; }
    }
    if (encKey is { Length: 32 } && Array.Exists(encKey, b => b != 0))
        tokenProtector = new AesGcmTokenProtector(encKey);
    else
        Log.Warning("Encryption:Key missing or a placeholder — WABA tokens & app secrets are stored " +
                    "UNENCRYPTED. Provision a real base64-encoded 32-byte key to enable at-rest encryption.");
    builder.Services.AddSingleton(tokenProtector);

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
    // Hoisted so the SignalR backplane below can reuse the same parsed options; null ⇒ Redis not configured.
    ConfigurationOptions? redisOptions = null;
    if (!string.IsNullOrWhiteSpace(redisConnection))
    {
        // Default AbortOnConnectFail=false (unless the string sets abortConnect explicitly): a brief Redis
        // outage at startup must NOT crash the app. The multiplexer reconnects in the background, and any
        // rate-limit call during the outage falls through to the bounded in-process fallback. Parsed once and
        // shared by the cache, the multiplexer and the SignalR backplane (Connect clones internally, so the
        // instance isn't mutated).
        redisOptions = ConfigurationOptions.Parse(redisConnection);
        if (!redisConnection.Contains("abortConnect", StringComparison.OrdinalIgnoreCase))
            redisOptions.AbortOnConnectFail = false;

        builder.Services.AddStackExchangeRedisCache(o => o.ConfigurationOptions = redisOptions);
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisOptions));
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
    builder.Services.AddTransient<TemplateStatusSyncJob>();
    builder.Services.AddTransient<wa_api.Features.Webhooks.Processing.WebhookProcessingJob>();
    builder.Services.AddTransient<wa_api.Features.Webhooks.Processing.WebhookSweeperJob>();
    builder.Services.AddTransient<wa_api.Features.Webhooks.Processing.WebhookHealthCheckJob>();
    builder.Services.AddTransient<wa_api.Features.Webhooks.Processing.WebhookRetentionJob>();

    // Named HttpClient for Meta Graph API calls (base address set here; auth header per-request).
    // ConnectCallback: try IPv4 before IPv6. graph.facebook.com publishes an AAAA record that some
    // networks blackhole, and .NET 8 has no Happy Eyeballs fallback — so every send hung for the full
    // 15s and failed, taking the WABA health check (and therefore the connection's Status) with it.
    // See DualStackConnect for the measurements.
    builder.Services.AddHttpClient("MetaGraph", c =>
    {
        c.BaseAddress = new Uri("https://graph.facebook.com/");
        c.Timeout = TimeSpan.FromSeconds(15);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectCallback = wa_api.Infrastructure.Http.DualStackConnect.ConnectAsync,
        // The factory rotates handlers every 2 minutes by default; keep pooled connections inside that
        // window so a rotated handler does not strand them.
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });

    // Separate client for downloading inbound media bytes — a longer timeout than the 15s API client since a
    // media payload (image/video/document) can be larger than a JSON call. Absolute lookaside URLs override the base.
    builder.Services.AddHttpClient("MetaMedia", c =>
    {
        c.BaseAddress = new Uri("https://graph.facebook.com/");
        c.Timeout = TimeSpan.FromSeconds(60);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectCallback = wa_api.Infrastructure.Http.DualStackConnect.ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });

    // ── Authentication & Authorization (JWT bearer) ───────────────────────
    builder.Services.AddPlatformAuthentication(builder.Configuration);

    // ── Rate limiting (H8) ─────────────────────────────────────────────────
    // Brute-force / password-spray protection on the anonymous auth endpoints. Sliding window per client IP
    // (accurate now that ForwardedHeaders restores the real IP behind Caddy). Rejections return 429. Applied
    // to /auth/login and /auth/refresh via [EnableRateLimiting("auth")].
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("auth", httpContext =>
            System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 15,
                    Window = TimeSpan.FromMinutes(5),
                    SegmentsPerWindow = 5,
                    QueueLimit = 0,
                }));
    });

    // ── Feature services ───────────────────────────────────────────────────
    builder.Services.AddScoped<wa_api.Features.Companies.ICompanyService, wa_api.Features.Companies.CompanyService>();
    builder.Services.AddScoped<wa_api.Features.WhatsApp.IWabaConnectionService, wa_api.Features.WhatsApp.WabaConnectionService>();
    builder.Services.AddScoped<wa_api.Features.WhatsApp.IMetaCredentialValidator, wa_api.Features.WhatsApp.MetaCredentialValidator>();
    builder.Services.AddScoped<wa_api.Features.Users.IUserService, wa_api.Features.Users.UserService>();
    builder.Services.AddScoped<wa_api.Features.Contacts.IContactService, wa_api.Features.Contacts.ContactService>();
    builder.Services.AddScoped<wa_api.Features.ContactLists.IContactListService, wa_api.Features.ContactLists.ContactListService>();
    // ── WABA send rate limiter (PRD 6.1) ──────────────────────────────────
    // Redis token bucket when configured (atomic across replicas); else an in-process bucket for
    // single-process/dev. The Redis limiter also degrades to the in-process bucket on a cache outage,
    // so the shared LocalBucketRegistry is always registered. Never fail-open.
    builder.Services.Configure<wa_api.Infrastructure.RateLimiting.RateLimitOptions>(
        builder.Configuration.GetSection("RateLimit"));
    builder.Services.AddSingleton<wa_api.Infrastructure.RateLimiting.LocalBucketRegistry>();
    builder.Services.AddSingleton<wa_api.Infrastructure.RateLimiting.InProcessTokenBucket>();
    if (!string.IsNullOrWhiteSpace(redisConnection))
        builder.Services.AddScoped<wa_api.Infrastructure.RateLimiting.IWabaRateLimiter,
            wa_api.Infrastructure.RateLimiting.RedisTokenBucketRateLimiter>();
    else
        builder.Services.AddSingleton<wa_api.Infrastructure.RateLimiting.IWabaRateLimiter>(
            sp => sp.GetRequiredService<wa_api.Infrastructure.RateLimiting.InProcessTokenBucket>());
    builder.Services.AddScoped<wa_api.Features.Messages.IMessageService, wa_api.Features.Messages.MessageService>();
    builder.Services.AddScoped<wa_api.Features.Messages.IWhatsAppMessageSender, wa_api.Features.Messages.WhatsAppMessageSender>();
    // Inbound media: the two-step Meta downloader + the async job that persists the bytes off the webhook tx.
    builder.Services.AddScoped<wa_api.Features.Messages.InboundMedia.IInboundMediaDownloader, wa_api.Features.Messages.InboundMedia.InboundMediaDownloader>();
    builder.Services.AddTransient<wa_api.Features.Messages.InboundMedia.InboundMediaDownloadJob>();
    builder.Services.AddScoped<wa_api.Features.Conversations.IConversationService, wa_api.Features.Conversations.ConversationService>();
    builder.Services.AddScoped<wa_api.Features.Conversations.Realtime.IChatNotifier, wa_api.Features.Conversations.Realtime.ChatNotifier>();

    // ── Real-time chat (SignalR) ───────────────────────────────────────────
    // camelCase payloads so the hub matches the REST API's JSON shape on the client.
    var signalRBuilder = builder.Services.AddSignalR()
        .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
    // Redis backplane so realtime chat survives horizontal scaling and blue/green deploy overlap. Without it,
    // a webhook processed on one replica pushes only to that replica's connected clients — tenants pinned to
    // another replica silently receive nothing until a manual refresh. Reuses the same parsed options
    // (AbortOnConnectFail=false) as the cache/lock multiplexer, so a brief Redis blip degrades rather than
    // crashes. No-op on a single instance (and in dev without Redis, where in-process delivery is correct).
    if (redisOptions is not null)
        signalRBuilder.AddStackExchangeRedis(o => o.Configuration = redisOptions);
    builder.Services.AddScoped<wa_api.Features.Plans.IPlanService, wa_api.Features.Plans.PlanService>();
    builder.Services.AddScoped<wa_api.Features.Packages.IPackageService, wa_api.Features.Packages.PackageService>();
    builder.Services.AddScoped<wa_api.Features.Subscriptions.ISubscriptionService, wa_api.Features.Subscriptions.SubscriptionService>();
    builder.Services.AddScoped<wa_api.Features.Templates.ITemplateService, wa_api.Features.Templates.TemplateService>();
    builder.Services.AddScoped<wa_api.Features.Templates.IMetaTemplateClient, wa_api.Features.Templates.MetaTemplateClient>();
    builder.Services.AddScoped<wa_api.Features.Templates.IMetaMediaUploader, wa_api.Features.Templates.MetaMediaUploader>();
    builder.Services.AddScoped<wa_api.Features.Templates.ITemplateMediaSampleStore, wa_api.Features.Templates.TemplateMediaSampleStore>();
    builder.Services.AddScoped<wa_api.Common.Subscriptions.ISubscriptionGate, wa_api.Common.Subscriptions.SubscriptionGate>();
    builder.Services.AddScoped<wa_api.Common.Subscriptions.ISubscriptionMeter, wa_api.Common.Subscriptions.SubscriptionMeter>();
    builder.Services.AddScoped<wa_api.Features.Campaigns.ICampaignService, wa_api.Features.Campaigns.CampaignService>();
    builder.Services.AddTransient<wa_api.Features.Campaigns.Jobs.CampaignLaunchJob>();
    builder.Services.AddTransient<wa_api.Features.Campaigns.Jobs.CampaignBatchSendJob>();
    builder.Services.AddTransient<wa_api.Features.Campaigns.Jobs.CampaignSchedulerJob>();
    builder.Services.AddTransient<wa_api.Features.Campaigns.Jobs.CampaignPoisonHandlerJob>();
    builder.Services.AddScoped<wa_api.Features.Notifications.INotificationService, wa_api.Features.Notifications.NotificationService>();
    builder.Services.AddTransient<wa_api.Features.Campaigns.Jobs.QuotaWarningCheckerJob>();
    builder.Services.AddScoped<wa_api.Features.Analytics.AnalyticsService>();
    // ── Chatbot gate (plan 005): platform switch + plan entitlement + "has the company set anything up" ──
    // Bot:Enabled ships false, so merging the chatbot in changes nothing until it is switched on.
    builder.Services.Configure<wa_api.Features.Bot.BotOptions>(
        builder.Configuration.GetSection(wa_api.Features.Bot.BotOptions.SectionName));
    builder.Services.AddScoped<wa_api.Common.Subscriptions.IPlanFeatureGate, wa_api.Common.Subscriptions.PlanFeatureGate>();
    builder.Services.AddScoped<wa_api.Features.Bot.IBotGate, wa_api.Features.Bot.BotGate>();

    // ── Chatbot: rule CRUD, the (pure) matcher, and the post-commit reply job ──
    builder.Services.AddScoped<wa_api.Features.Bot.IAutoReplyRuleService, wa_api.Features.Bot.AutoReplyRuleService>();
    builder.Services.AddScoped<wa_api.Features.Bot.IBotProfileService, wa_api.Features.Bot.BotProfileService>();
    builder.Services.AddScoped<wa_api.Features.Bot.IBotPreviewService, wa_api.Features.Bot.BotPreviewService>();
    builder.Services.AddScoped<wa_api.Features.Bot.StaffAlerts.IStaffAlertService, wa_api.Features.Bot.StaffAlerts.StaffAlertService>();
    builder.Services.AddSingleton<wa_api.Features.Bot.IAutoReplyEngine, wa_api.Features.Bot.AutoReplyEngine>();
    builder.Services.AddTransient<wa_api.Features.Bot.Jobs.AutoReplyJob>();

    // ── Chatbot: the AI sales engine ──────────────────────────────────────
    // The key belongs in user-secrets or an environment variable. appsettings.Development.json is
    // COMMITTED in this repo, so a key placed there would be published the moment it was written.
    // An absent key is a supported state: the AI path skips itself and the keyword rules keep replying.
    builder.Services.Configure<wa_api.Features.Bot.Ai.AnthropicOptions>(
        builder.Configuration.GetSection(wa_api.Features.Bot.Ai.AnthropicOptions.SectionName));

    var anthropicTimeout = builder.Configuration.GetValue<int?>("Anthropic:TimeoutSeconds") ?? 20;
    builder.Services.AddHttpClient(wa_api.Features.Bot.Ai.AnthropicOptions.HttpClientName, c =>
    {
        // The customer is waiting on the other end of a WhatsApp thread, and Hangfire's retry is the
        // backstop for a slow upstream — so failing fast beats holding one of only four workers.
        c.Timeout = TimeSpan.FromSeconds(anthropicTimeout);
    });

    // Singleton: it builds one AnthropicClient and holds no per-request state. The tool executor, which
    // does hold per-conversation state, is constructed by the job instead of registered.
    builder.Services.AddSingleton<wa_api.Features.Bot.Ai.IAiReplyPlanner, wa_api.Features.Bot.Ai.AiReplyPlanner>();
    builder.Services.AddTransient<wa_api.Features.Bot.Jobs.AiReplyJob>();
    builder.Services.AddTransient<wa_api.Features.Bot.Jobs.BotAutoResumeJob>();
    builder.Services.AddTransient<wa_api.Features.Bot.Jobs.BotFollowUpJob>();
    // Fills a template's variables from the business details before a follow-up goes out as one.
    builder.Services.AddScoped<wa_api.Features.Bot.Ai.TemplateVariableResolver>();

    // ── Cost tracking (plan 013): every Anthropic call and every billed Meta message gets a USD cost,
    // priced from config at write time. Calculators are stateless; the logger writes through the scoped DbContext.
    builder.Services.AddSingleton<wa_api.Features.Bot.Ai.IAiCostCalculator, wa_api.Features.Bot.Ai.AiCostCalculator>();
    builder.Services.AddScoped<wa_api.Features.Bot.Ai.IAiUsageLogger, wa_api.Features.Bot.Ai.AiUsageLogger>();
    builder.Services.Configure<wa_api.Features.Messages.MetaPricingOptions>(
        builder.Configuration.GetSection(wa_api.Features.Messages.MetaPricingOptions.SectionName));
    builder.Services.AddSingleton<wa_api.Features.Messages.IMetaCostCalculator, wa_api.Features.Messages.MetaCostCalculator>();

    // ── Calendar: configuration, the (pure) slot calculator, and bookings ──
    // The calculator is stateless and does no I/O, so it is a singleton exactly like the auto-reply matcher.
    builder.Services.AddSingleton<wa_api.Features.Calendar.Availability.IAvailabilityCalculator,
                                  wa_api.Features.Calendar.Availability.AvailabilityCalculator>();
    builder.Services.AddScoped<wa_api.Features.Calendar.ICalendarSettingsService, wa_api.Features.Calendar.CalendarSettingsService>();
    builder.Services.AddScoped<wa_api.Features.Calendar.IAppointmentTypeService, wa_api.Features.Calendar.AppointmentTypeService>();
    builder.Services.AddScoped<wa_api.Features.Calendar.IAvailabilityService, wa_api.Features.Calendar.AvailabilityService>();
    builder.Services.AddScoped<wa_api.Features.Calendar.IAppointmentService, wa_api.Features.Calendar.AppointmentService>();

    // ── Clinic CRM (plan 014): a connected company's bookings go to its clinic's CRM first ──
    // The token cache is a singleton so every request and job shares one token per connection — the CRM
    // allows only 10 token requests a minute.
    builder.Services.Configure<wa_api.Features.ClinicCrm.ClinicCrmOptions>(
        builder.Configuration.GetSection(wa_api.Features.ClinicCrm.ClinicCrmOptions.SectionName));
    builder.Services.AddSingleton<wa_api.Features.ClinicCrm.Client.ClinicCrmTokenCache>();
    builder.Services.AddHttpClient<wa_api.Features.ClinicCrm.Client.IClinicCrmClient,
                                   wa_api.Features.ClinicCrm.Client.ClinicCrmClient>((sp, c) =>
    {
        var crmOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<wa_api.Features.ClinicCrm.ClinicCrmOptions>>();
        c.Timeout = TimeSpan.FromSeconds(Math.Max(1, crmOptions.Value.TimeoutSeconds));
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectCallback = wa_api.Infrastructure.Http.DualStackConnect.ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });
    builder.Services.AddScoped<wa_api.Features.ClinicCrm.IClinicCrmChatSummarizer, wa_api.Features.ClinicCrm.ClinicCrmChatSummarizer>();
    builder.Services.AddScoped<wa_api.Features.ClinicCrm.IClinicCrmBookingService, wa_api.Features.ClinicCrm.ClinicCrmBookingService>();
    builder.Services.AddScoped<wa_api.Features.ClinicCrm.IClinicCrmSettingsService, wa_api.Features.ClinicCrm.ClinicCrmSettingsService>();
    builder.Services.AddTransient<wa_api.Features.ClinicCrm.Jobs.ClinicCrmSyncJob>();
    builder.Services.AddTransient<wa_api.Features.ClinicCrm.Jobs.ClinicCrmLeadJob>();

    builder.Services.AddScoped<wa_api.Features.Monitoring.MonitoringService>();
    builder.Services.AddScoped<wa_api.Features.AdminReports.AdminReportsService>();

    // ── Stripe (PRD 3.2 / 3.3 / 3.5) ─────────────────────────────────────
    builder.Services.Configure<wa_api.Infrastructure.Stripe.StripeOptions>(
        builder.Configuration.GetSection("Stripe"));
    builder.Services.AddScoped<wa_api.Infrastructure.Stripe.IStripeService, wa_api.Infrastructure.Stripe.StripeService>();
    builder.Services.AddScoped<wa_api.Features.Billing.IInvoiceService, wa_api.Features.Billing.InvoiceService>();
    builder.Services.AddTransient<wa_api.Features.Billing.Jobs.StripeWebhookProcessingJob>();

    // ── PayHere (self-service checkout, one-time payment per subscribe/renew) ──
    builder.Services.Configure<wa_api.Infrastructure.PayHere.PayHereOptions>(
        builder.Configuration.GetSection("PayHere"));
    builder.Services.AddScoped<wa_api.Infrastructure.PayHere.IPayHereService, wa_api.Infrastructure.PayHere.PayHereService>();
    builder.Services.AddTransient<wa_api.Features.Subscriptions.Jobs.PayHereWebhookProcessingJob>();

    // ── FX (display-only USD→AED, for showing a dirham equivalent to the UAE audience) ──
    // Short timeout on purpose: this decorates a price, so a slow provider must never hold up the
    // plans response — FxRateService falls back to the peg instead.
    builder.Services.AddMemoryCache();
    builder.Services.AddHttpClient(wa_api.Infrastructure.Fx.FxRateService.HttpClientName, c =>
    {
        c.BaseAddress = new Uri("https://open.er-api.com/");
        c.Timeout = TimeSpan.FromSeconds(5);
    });
    builder.Services.AddScoped<wa_api.Infrastructure.Fx.IFxRateService, wa_api.Infrastructure.Fx.FxRateService>();

    // ── Webhooks (Meta inbound: template status 5.2 + delivery status 6.4; inbound stub → Phase 7) ──
    builder.Services.AddScoped<wa_api.Features.Webhooks.Signature.IMetaSignatureVerifier, wa_api.Features.Webhooks.Signature.MetaSignatureVerifier>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Ingestion.IWebhookInboxService, wa_api.Features.Webhooks.Ingestion.WebhookInboxService>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Processing.IWebhookDispatcher, wa_api.Features.Webhooks.Processing.WebhookDispatcher>();
    // Handlers queue their realtime pushes here; WebhookProcessingJob runs them after the batch commits.
    builder.Services.AddScoped<wa_api.Features.Webhooks.Processing.WebhookPostCommitActions>();
    // All handlers share one interface so the dispatcher receives them via IEnumerable<IWebhookEventHandler>.
    builder.Services.AddScoped<wa_api.Features.Webhooks.Handlers.IWebhookEventHandler, wa_api.Features.Webhooks.Handlers.TemplateStatusWebhookHandler>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Handlers.IWebhookEventHandler, wa_api.Features.Webhooks.Handlers.MessageStatusWebhookHandler>();
    builder.Services.AddScoped<wa_api.Features.Webhooks.Handlers.IWebhookEventHandler, wa_api.Features.Webhooks.Handlers.InboundMessageWebhookHandler>();
    // Phase 7 (Conversation module): InboundMessageWebhookHandler now upserts the contact + conversation,
    // writes the inbound message, and pushes a realtime event via IChatNotifier (mapped to ChatHub below).

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
            options.JsonSerializerOptions.Converters.Add(new wa_api.Common.Json.UtcDateTimeConverter());
            options.JsonSerializerOptions.Converters.Add(new wa_api.Common.Json.NullableUtcDateTimeConverter());
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

    // ── CORS (browser SPA + SignalR WebSocket need explicit origins WITH credentials) ──
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:3000"];
    builder.Services.AddCors(options =>
        options.AddPolicy("spa", policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

    var app = builder.Build();

    // ── Migrate & seed (dev/staging only) ─────────────────────────────────
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        await DataSeeder.SeedAsync(app.Services, app.Logger);
    }

    // Encrypt any WABA secrets still stored as plaintext (H3). Idempotent; no-op without an encryption key.
    // All environments — this is how existing prod rows reach rest-encrypted after the key is provisioned.
    await WabaSecretEncryptionBackfill.RunAsync(app.Services, app.Logger);

    // ── Middleware Pipeline (ORDER MATTERS) ───────────────────────────────
    app.UseForwardedHeaders();                       // 0. Restore real client IP/scheme from Caddy (before all)
    app.UseMiddleware<GlobalExceptionMiddleware>();  // 1. Catch all exceptions → ApiError
    app.UseMiddleware<CorrelationIdMiddleware>();    // 2. Correlation ID (sets context.Items)
    app.UseSerilogRequestLogging();                  // 3. Log every request with correlation id

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WhatsApp API v1"));
    }

    if (!app.Environment.IsDevelopment())
        app.UseHttpsRedirection();
    app.UseCors("spa");                              // before auth; SignalR negotiate needs it
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();                            // after routing/auth; gates [EnableRateLimiting] endpoints
    app.MapControllers();
    app.MapHub<wa_api.Features.Conversations.Realtime.ChatHub>("/hubs/chat");
    app.MapPlatformHealthChecks();

    // ── Hangfire dashboard + recurring jobs ────────────────────────────────
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter(app.Environment)]
    });
    RecurringJob.AddOrUpdate<HeartbeatJob>("heartbeat", j => j.Run(), Cron.Hourly);
    RecurringJob.AddOrUpdate<WabaHealthCheckJob>("waba-health-check", j => j.RunAsync(), Cron.MinuteInterval(15));
    // Retired: the balance now runs continuously until expiry (accumulate-until-expiry model),
    // so there is no monthly usage reset. RemoveIfExists cleans up any schedule left in Hangfire.
    RecurringJob.RemoveIfExists("subscription-period-reset");
    RecurringJob.AddOrUpdate<TemplateStatusSyncJob>("template-status-sync", j => j.RunAsync(), Cron.MinuteInterval(15));
    // Hands paused threads back to the bot when their takeover window expires, so an agent who replied
    // once and moved on does not silently orphan the conversation.
    RecurringJob.AddOrUpdate<wa_api.Features.Bot.Jobs.BotAutoResumeJob>(
        "bot-auto-resume", j => j.RunAsync(CancellationToken.None), Cron.MinuteInterval(5));
    RecurringJob.AddOrUpdate<wa_api.Features.Webhooks.Processing.WebhookSweeperJob>("webhook-sweeper", j => j.RunAsync(), Cron.MinuteInterval(5));
    RecurringJob.AddOrUpdate<wa_api.Features.Webhooks.Processing.WebhookHealthCheckJob>("webhook-health-check", j => j.RunAsync(), Cron.MinuteInterval(5));
    RecurringJob.AddOrUpdate<wa_api.Features.Webhooks.Processing.WebhookRetentionJob>("webhook-retention", j => j.RunAsync(CancellationToken.None), "30 3 * * *");
    RecurringJob.AddOrUpdate<wa_api.Features.Campaigns.Jobs.CampaignSchedulerJob>("campaign-scheduler", j => j.RunAsync(), Cron.Minutely);
    RecurringJob.AddOrUpdate<wa_api.Features.Campaigns.Jobs.QuotaWarningCheckerJob>("quota-warning-checker", j => j.RunAsync(CancellationToken.None), "0 6 * * *");

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
