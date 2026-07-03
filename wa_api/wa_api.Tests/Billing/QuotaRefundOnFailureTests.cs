using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Features.Webhooks.Handlers;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Billing;

/// <summary>
/// Pins the refund-on-failure billing rule: a message metered on accept (Sent) that Meta later reports
/// as failed via the delivery-status webhook must credit one message back to the company's quota, exactly
/// once. Guards the two ways this can go wrong: double-refunding on a redelivered callback, and refunding
/// a message that was never counted. Backed by EF Core InMemory (the handler uses tracked updates, so the
/// refund is flushed by the caller's SaveChanges — mirrored here).
/// </summary>
public class QuotaRefundOnFailureTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb(Guid companyId) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"quota-refund-{Guid.NewGuid()}")
                .Options,
            new Tenant(companyId));

    private static Subscription NewSub(Guid companyId, int used) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PlanId = Guid.NewGuid(),
        Status = SubscriptionStatus.Active,
        CurrentPeriodStart = DateTime.UtcNow.AddDays(-1),
        CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        MessagesUsedThisPeriod = used,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static Message NewMessage(Guid companyId, string wamid, MessageStatus status) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ContactId = Guid.NewGuid(),
        WabaConnectionId = Guid.NewGuid(),
        Body = "hi",
        Direction = MessageDirection.Outbound,
        Status = status,
        ExternalMessageId = wamid,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static WebhookContext Status(string wamid, string status, long? errorCode = null) => new()
    {
        Field = "messages",
        Change = new WebhookChange
        {
            Field = "messages",
            Value = new WebhookValue
            {
                Statuses =
                [
                    new WebhookStatus
                    {
                        Id = wamid,
                        Status = status,
                        Timestamp = "1700000000",
                        Errors = errorCode is null
                            ? null
                            : [new WebhookError { Code = errorCode, Title = "failed", Message = "not delivered" }],
                    },
                ],
            },
        },
    };

    private static MessageStatusWebhookHandler NewHandler(AppDbContext db) =>
        new(db, NullLogger<MessageStatusWebhookHandler>.Instance);

    [Fact]
    public async Task FailedStatus_CreditsOneMessageBack()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        db.Subscriptions.Add(NewSub(companyId, used: 5));
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent));
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync(); // mirrors the dispatcher's single atomic commit

        var msg = await db.Messages.IgnoreQueryFilters().FirstAsync();
        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(MessageStatus.Failed, msg.Status);
        Assert.Equal("131049", msg.ErrorCode);
        Assert.Equal(4, sub.MessagesUsedThisPeriod); // 5 → 4
    }

    [Fact]
    public async Task DuplicateFailedCallback_RefundsOnlyOnce()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        db.Subscriptions.Add(NewSub(companyId, used: 5));
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent));
        await db.SaveChangesAsync();

        var handler = NewHandler(db);
        await handler.HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();
        // Meta re-delivers the same 'failed' status — must not refund a second time.
        await handler.HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(4, sub.MessagesUsedThisPeriod); // 4, not 3
    }

    [Fact]
    public async Task DeliveredStatus_DoesNotRefund()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        db.Subscriptions.Add(NewSub(companyId, used: 5));
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent));
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "delivered"));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().FirstAsync();
        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(MessageStatus.Delivered, msg.Status);
        Assert.Equal(5, sub.MessagesUsedThisPeriod); // unchanged — a successful delivery still counts
    }

    [Fact]
    public async Task FailedStatus_NeverDrivesUsageNegative()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        db.Subscriptions.Add(NewSub(companyId, used: 0));
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent));
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(0, sub.MessagesUsedThisPeriod); // floored at 0, not -1
    }
}
