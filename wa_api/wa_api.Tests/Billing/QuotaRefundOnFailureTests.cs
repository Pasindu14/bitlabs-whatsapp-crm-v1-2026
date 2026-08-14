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

    /// <param name="meteredSubId">
    /// The subscription this send reserved a credit against. Null models a send that consumed nothing —
    /// a free reply inside the 24-hour customer-service window — which must never be refunded.
    /// </param>
    private static Message NewMessage(
        Guid companyId, string wamid, MessageStatus status, Guid? meteredSubId = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ContactId = Guid.NewGuid(),
        WabaConnectionId = Guid.NewGuid(),
        Body = "hi",
        Direction = MessageDirection.Outbound,
        Status = status,
        ExternalMessageId = wamid,
        MeteredSubscriptionId = meteredSubId,
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
        var sub0 = NewSub(companyId, used: 5);
        db.Subscriptions.Add(sub0);
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent, sub0.Id));
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
        var sub0 = NewSub(companyId, used: 5);
        db.Subscriptions.Add(sub0);
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent, sub0.Id));
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
        var sub0 = NewSub(companyId, used: 5);
        db.Subscriptions.Add(sub0);
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent, sub0.Id));
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
        var sub0 = NewSub(companyId, used: 0);
        db.Subscriptions.Add(sub0);
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent, sub0.Id));
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(0, sub.MessagesUsedThisPeriod); // floored at 0, not -1
    }

    [Fact] // H5: Meta billed this send, so a later failure must NOT credit the quota back.
    public async Task BilledMessage_ThatFails_IsNotRefunded()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var sub0 = NewSub(companyId, used: 5);
        db.Subscriptions.Add(sub0);
        // Stamped as metered, so Billable is the ONLY reason the refund is withheld.
        var m = NewMessage(companyId, "wamid-1", MessageStatus.Sent, sub0.Id);
        m.Billable = true; // Meta charged for it
        db.Messages.Add(m);
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(5, sub.MessagesUsedThisPeriod); // unchanged — billed messages aren't refunded
    }

    [Fact] // Forward-only: a late 'failed' for an already-Delivered message must not regress or refund it.
    public async Task LateFailedAfterDelivered_DoesNotRefundOrRegress()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var sub0 = NewSub(companyId, used: 5);
        db.Subscriptions.Add(sub0);
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Delivered, sub0.Id));
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().FirstAsync();
        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(MessageStatus.Delivered, msg.Status); // not regressed to Failed
        Assert.Equal(5, sub.MessagesUsedThisPeriod);       // not refunded
    }

    [Fact] // H2: refund the subscription the message was metered against, not merely the newest active one.
    public async Task Refund_TargetsMeteredSubscription_NotNewestActive()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var subA = NewSub(companyId, used: 5);
        subA.CreatedAt = DateTime.UtcNow.AddDays(-10); // older — the one that was charged
        var subB = NewSub(companyId, used: 2);
        subB.CreatedAt = DateTime.UtcNow;              // newer active — must NOT be touched
        db.Subscriptions.AddRange(subA, subB);
        var m = NewMessage(companyId, "wamid-1", MessageStatus.Sent);
        m.MeteredSubscriptionId = subA.Id;
        db.Messages.Add(m);
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var a = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == subA.Id);
        var b = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == subB.Id);
        Assert.Equal(4, a.MessagesUsedThisPeriod); // metered sub refunded
        Assert.Equal(2, b.MessagesUsedThisPeriod); // newer active untouched
    }

    [Fact] // A free 24h-window reply reserved nothing, so a later failure must not credit quota back.
    public async Task UnmeteredMessage_ThatFails_IsNotRefunded()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        db.Subscriptions.Add(NewSub(companyId, used: 5));
        // MeteredSubscriptionId left null — the free-send signature.
        db.Messages.Add(NewMessage(companyId, "wamid-1", MessageStatus.Sent));
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(Status("wamid-1", "failed", 131049));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().FirstAsync();
        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(MessageStatus.Failed, msg.Status); // status still advances
        Assert.Equal(5, sub.MessagesUsedThisPeriod);    // but no phantom credit
    }
}
