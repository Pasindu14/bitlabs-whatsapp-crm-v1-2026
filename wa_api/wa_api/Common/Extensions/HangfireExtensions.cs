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

        services.AddHangfireServer();
        return services;
    }
}
