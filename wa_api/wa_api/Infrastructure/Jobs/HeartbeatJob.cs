namespace wa_api.Infrastructure.Jobs;

/// <summary>Liveness job — proves the Hangfire recurring scheduler is processing work.</summary>
public class HeartbeatJob(ILogger<HeartbeatJob> logger)
{
    public void Run() => logger.LogInformation("Hangfire heartbeat at {Time:o}", DateTime.UtcNow);
}
