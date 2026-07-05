using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Webhooks.Entities;
using wa_api.Features.Webhooks.Ingestion;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Webhooks;

/// <summary>
/// Pins the sweeper's reclaim query (M13). A Failed row is owned by Hangfire's own retry backoff (max 900s),
/// so re-enqueuing it within that window double-drives the row and inflates Attempts, dead-lettering it early.
/// GetStuckIdsAsync therefore reclaims Received/Processing rows after the short stuck-threshold but only sweeps
/// Failed rows once they are clearly orphaned (older than the whole retry schedule).
/// </summary>
public class WebhookSweepQueryTests
{
    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => null;   // the inbox is not tenant-scoped
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"webhook-sweep-{Guid.NewGuid()}")
                .Options,
            new Tenant());

    private static WhatsAppWebhookEvent Event(WebhookEventStatus status, TimeSpan age) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        UpdatedAt = DateTime.UtcNow - age,
        ReceivedAt = DateTime.UtcNow - age,
        PayloadJson = "{}",
    };

    [Fact]
    public async Task ReclaimsAgedReceivedAndProcessing_ButNotFresh()
    {
        await using var db = NewDb();
        var agedReceived = Event(WebhookEventStatus.Received, TimeSpan.FromMinutes(10));
        var agedProcessing = Event(WebhookEventStatus.Processing, TimeSpan.FromMinutes(10));
        var freshReceived = Event(WebhookEventStatus.Received, TimeSpan.FromMinutes(1));
        db.WhatsAppWebhookEvents.AddRange(agedReceived, agedProcessing, freshReceived);
        await db.SaveChangesAsync();

        var stuck = await new WebhookInboxService(db).GetStuckIdsAsync(TimeSpan.FromMinutes(5), 100);

        Assert.Contains(agedReceived.Id, stuck);
        Assert.Contains(agedProcessing.Id, stuck);
        Assert.DoesNotContain(freshReceived.Id, stuck);
    }

    [Fact]
    public async Task FailedRow_WithinRetryWindow_IsNotSwept()
    {
        // 10 min old — still inside Hangfire's retry schedule; the sweeper must leave it alone.
        await using var db = NewDb();
        var failedInWindow = Event(WebhookEventStatus.Failed, TimeSpan.FromMinutes(10));
        db.WhatsAppWebhookEvents.Add(failedInWindow);
        await db.SaveChangesAsync();

        var stuck = await new WebhookInboxService(db).GetStuckIdsAsync(TimeSpan.FromMinutes(5), 100);

        Assert.DoesNotContain(failedInWindow.Id, stuck);
    }

    [Fact]
    public async Task FailedRow_PastRetryWindow_IsSwept()
    {
        // 25 min old — past the entire retry schedule (20 min orphan threshold); genuinely orphaned, reclaim it.
        await using var db = NewDb();
        var orphanedFailed = Event(WebhookEventStatus.Failed, TimeSpan.FromMinutes(25));
        db.WhatsAppWebhookEvents.Add(orphanedFailed);
        await db.SaveChangesAsync();

        var stuck = await new WebhookInboxService(db).GetStuckIdsAsync(TimeSpan.FromMinutes(5), 100);

        Assert.Contains(orphanedFailed.Id, stuck);
    }

    [Fact]
    public async Task TerminalRows_AreNeverSwept()
    {
        await using var db = NewDb();
        var processed = Event(WebhookEventStatus.Processed, TimeSpan.FromHours(1));
        var dead = Event(WebhookEventStatus.Dead, TimeSpan.FromHours(1));
        db.WhatsAppWebhookEvents.AddRange(processed, dead);
        await db.SaveChangesAsync();

        var stuck = await new WebhookInboxService(db).GetStuckIdsAsync(TimeSpan.FromMinutes(5), 100);

        Assert.Empty(stuck);
    }
}
