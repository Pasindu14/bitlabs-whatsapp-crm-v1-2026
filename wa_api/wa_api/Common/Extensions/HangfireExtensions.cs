using Hangfire;
using Hangfire.Redis.StackExchange;

namespace wa_api.Common.Extensions;

public static class HangfireExtensions
{
    /// <summary>
    /// Registers Hangfire + its server. Uses Redis storage when <c>REDIS_CONNECTION</c>
    /// is set, otherwise falls back to in-memory storage (dev / no-Redis).
    /// </summary>
    public static IServiceCollection AddPlatformHangfire(this IServiceCollection services, IConfiguration config)
    {
        var redis = config["REDIS_CONNECTION"];

        services.AddHangfire(cfg =>
        {
            cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
               .UseSimpleAssemblyNameTypeSerializer()
               .UseRecommendedSerializerSettings();

            if (!string.IsNullOrWhiteSpace(redis))
                cfg.UseRedisStorage(redis,
                    new Hangfire.Redis.StackExchange.RedisStorageOptions { Prefix = "hangfire:" });
            else
                cfg.UseInMemoryStorage();
        });

        // Dedicated "webhooks" queue (listed first = higher priority, drained before "default") so webhook
        // processing latency isn't starved by the recurring poll / health-check jobs.
        //
        // WorkerCount is deliberately capped low. Jobs reach Postgres through the SAME scoped AppDbContext
        // pool as HTTP requests (Maximum Pool Size=10, sized under Supavisor's 15-client cap), so cores×2
        // workers (8 on a 4-vCPU box) could hold most of the pool during a campaign burst and starve the API.
        // Campaign send throughput is gated by Meta's rate limiter, not worker count, so a small pool loses no
        // real throughput. Raise Hangfire:WorkerCount only after moving prod to Supavisor transaction mode
        // (port 6543) and enlarging the app pool.
        var workerCount = config.GetValue<int?>("Hangfire:WorkerCount") ?? 4;
        services.AddHangfireServer(options =>
        {
            options.Queues = ["webhooks", "default"];
            options.WorkerCount = workerCount;
        });
        return services;
    }
}
