using Microsoft.EntityFrameworkCore;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Infrastructure.Caching;

/// <summary>Hourly background sweep of expired idempotency keys.</summary>
public class IdempotencyCleanupService(IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<IdempotencyCleanupService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await db.IdempotencyKeys
                    .Where(x => x.ExpiresAt < DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    _logger.LogInformation("Cleaned up {Count} expired idempotency keys", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Idempotency cleanup failed");
            }
        }
    }
}
