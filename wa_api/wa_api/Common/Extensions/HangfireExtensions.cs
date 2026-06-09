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
        // processing latency isn't starved by the recurring poll / health-check jobs. Raised worker count.
        services.AddHangfireServer(options =>
        {
            options.Queues = ["webhooks", "default"];
            options.WorkerCount = Math.Max(4, Environment.ProcessorCount * 2);
        });
        return services;
    }
}
