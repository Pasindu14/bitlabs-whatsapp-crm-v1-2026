using Microsoft.EntityFrameworkCore;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Infrastructure.Jobs;

/// <summary>
/// Rolls every active subscription whose <see cref="Subscription.CurrentPeriodEnd"/> has passed
/// into a fresh period: zeroes the usage counter and advances the window. Without Stripe's billing
/// cycle to lean on (PRD 3.1, Stripe deferred), this is what makes monthly quotas actually reset.
/// Runs cross-tenant — bypasses the EF query filter intentionally.
/// </summary>
public class SubscriptionPeriodResetJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionPeriodResetJob> logger)
{
    private const int PeriodDays = 30;

    public async Task RunAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var due = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= now)
            .ToListAsync();

        if (due.Count == 0)
        {
            logger.LogInformation("SubscriptionPeriodReset: no subscriptions due for reset.");
            return;
        }

        foreach (var sub in due)
        {
            // Advance from the previous end so periods stay aligned even if the job runs late.
            sub.CurrentPeriodStart = sub.CurrentPeriodEnd;
            sub.CurrentPeriodEnd = sub.CurrentPeriodEnd.AddDays(PeriodDays);
            // If the job was down for multiple periods, catch up so the new end is in the future.
            while (sub.CurrentPeriodEnd <= now)
            {
                sub.CurrentPeriodStart = sub.CurrentPeriodEnd;
                sub.CurrentPeriodEnd = sub.CurrentPeriodEnd.AddDays(PeriodDays);
            }
            sub.MessagesUsedThisPeriod = 0;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("SubscriptionPeriodReset: rolled {Count} subscription(s) at {Time:o}.",
            due.Count, now);
    }
}
