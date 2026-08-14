using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Dtos;
using wa_api.Features.Messages.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Billing;

/// <summary>
/// Pins the free-window billing rule: a reply sent inside the open 24-hour customer-service window costs
/// NO message credit, because Meta bills that window as a single conversation rather than per message.
/// Only the business-initiated send that opens a cold thread is charged.
/// <para>
/// These tests assert on whether the <em>reservation</em> happened, not merely on the resulting counter —
/// a free send must never enter the meter at all, which is what lets an agent keep replying on an
/// exhausted balance.
/// </para>
/// </summary>
public class FreeWindowBillingTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    /// <summary>
    /// Records what the send pathway was asked to charge, without touching Meta or the meter. Persists the
    /// Message like the real sender does, so callers can assert on the stored row.
    /// </summary>
    private sealed class SpySender(AppDbContext db) : IWhatsAppMessageSender
    {
        public List<bool> ChargeCredits { get; } = [];

        public async Task<Message> SendAsync(
            Contact contact, WabaConnection waba, string body, Guid? conversationId,
            bool chargeCredit, CancellationToken ct = default)
        {
            ChargeCredits.Add(chargeCredit);
            var message = new Message
            {
                Id = Guid.NewGuid(),
                CompanyId = contact.CompanyId,
                ContactId = contact.Id,
                WabaConnectionId = waba.Id,
                ConversationId = conversationId,
                Body = body,
                Direction = MessageDirection.Outbound,
                Status = MessageStatus.Accepted,
                ExternalMessageId = "wamid-" + Guid.NewGuid(),
                // The real sender stamps this only when it reserved — mirror that, since it is the
                // signal the delivery-failure refund path keys off.
                MeteredSubscriptionId = chargeCredit ? Guid.NewGuid() : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
            };

            db.Messages.Add(message);
            await db.SaveChangesAsync(ct);
            return message;
        }
    }

    private sealed class NoopNotifier : IChatNotifier
    {
        public Task MessageAsync(
            Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task MessageDeletedAsync(
            Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AppDbContext NewDb(Guid companyId) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"free-window-{Guid.NewGuid()}")
                .Options,
            new Tenant(companyId));

    private static Contact NewContact(Guid companyId) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Phone = "94770000001",
        Name = "Test Contact",
        IsWhatsAppValid = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static WabaConnection NewWaba(Guid companyId) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PhoneNumberId = "pn-1",
        WabaId = "waba-1",
        DisplayPhoneNumber = "+1 555 0100",
        EncryptedAccessToken = "token",
        Status = WabaConnectionStatus.Connected,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static Conversation NewConversation(
        Guid companyId, Contact contact, WabaConnection waba, DateTime? windowExpiresAt) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ContactId = contact.Id,
        WabaConnectionId = waba.Id,
        Status = ConversationStatus.Open,
        LastMessageAt = DateTime.UtcNow.AddMinutes(-5),
        LastMessageDirection = MessageDirection.Inbound,
        WindowExpiresAt = windowExpiresAt,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    [Fact] // The whole point: replies inside the customer-service window are free.
    public async Task ReplyInsideOpenWindow_TakesNoReservation()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var contact = NewContact(companyId);
        var waba = NewWaba(companyId);
        var conversation = NewConversation(companyId, contact, waba, DateTime.UtcNow.AddHours(12));
        db.AddRange(contact, waba, conversation);
        await db.SaveChangesAsync();

        var sender = new SpySender(db);
        var svc = new ConversationService(db, sender, new NoopNotifier());

        await svc.SendMessageAsync(conversation.Id, "first reply");
        await svc.SendMessageAsync(conversation.Id, "second reply");
        await svc.SendMessageAsync(conversation.Id, "third reply");

        Assert.Equal([false, false, false], sender.ChargeCredits);
    }

    [Fact] // A rolling window: each inbound pushes it out, so a reply a day later is still free.
    public async Task ReplyAfterWindowRolledForward_IsStillFree()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var contact = NewContact(companyId);
        var waba = NewWaba(companyId);
        // Opened yesterday, then pushed forward by a fresh inbound an hour ago.
        var conversation = NewConversation(companyId, contact, waba, DateTime.UtcNow.AddHours(23));
        db.AddRange(contact, waba, conversation);
        await db.SaveChangesAsync();

        var sender = new SpySender(db);
        var svc = new ConversationService(db, sender, new NoopNotifier());

        await svc.SendMessageAsync(conversation.Id, "reply");

        Assert.Equal([false], sender.ChargeCredits);
    }

    // Business-initiated: a cold contact has no window, so the send is presented to the meter as charged.
    // In production Meta then rejects free-form text outside a window (131047) and WhatsAppMessageSender
    // refunds the reservation — so the real-world cost is 0 and template campaigns are effectively the only
    // thing that consumes credits. What's pinned here is the DECISION, which is what would silently flip if
    // someone keyed the rule off the wrong field.
    [Fact]
    public async Task StartConversation_WithColdContact_IsPresentedAsCharged()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var contact = NewContact(companyId);
        var waba = NewWaba(companyId);
        db.AddRange(contact, waba);
        await db.SaveChangesAsync();

        var sender = new SpySender(db);
        var svc = new ConversationService(db, sender, new NoopNotifier());

        await svc.StartConversationAsync(contact.Id, waba.Id, "hello there");

        Assert.Equal([true], sender.ChargeCredits);
    }

    [Fact] // Reusing a thread whose window is live is a reply, not outreach — bill it as one.
    public async Task StartConversation_ReusingLiveWindow_IsFree()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var contact = NewContact(companyId);
        var waba = NewWaba(companyId);
        var conversation = NewConversation(companyId, contact, waba, DateTime.UtcNow.AddHours(6));
        db.AddRange(contact, waba, conversation);
        await db.SaveChangesAsync();

        var sender = new SpySender(db);
        var svc = new ConversationService(db, sender, new NoopNotifier());

        await svc.StartConversationAsync(contact.Id, waba.Id, "following up");

        Assert.Equal([false], sender.ChargeCredits);
    }

    [Fact] // An expired window blocks the send outright, so no credit is spent on a doomed attempt.
    public async Task ReplyOnExpiredWindow_IsRejectedBeforeAnySend()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var contact = NewContact(companyId);
        var waba = NewWaba(companyId);
        var conversation = NewConversation(companyId, contact, waba, DateTime.UtcNow.AddMinutes(-1));
        db.AddRange(contact, waba, conversation);
        await db.SaveChangesAsync();

        var sender = new SpySender(db);
        var svc = new ConversationService(db, sender, new NoopNotifier());

        var ex = await Assert.ThrowsAsync<wa_api.Common.Errors.BusinessRuleException>(
            () => svc.SendMessageAsync(conversation.Id, "too late"));

        Assert.Equal("WINDOW_CLOSED", ex.ErrorCode);
        Assert.Empty(sender.ChargeCredits);
    }

    [Fact] // The free send leaves no metered stamp, which is what stops the failure webhook refunding it.
    public async Task FreeReply_LeavesMeteredSubscriptionNull()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var contact = NewContact(companyId);
        var waba = NewWaba(companyId);
        var conversation = NewConversation(companyId, contact, waba, DateTime.UtcNow.AddHours(3));
        db.AddRange(contact, waba, conversation);
        await db.SaveChangesAsync();

        var svc = new ConversationService(db, new SpySender(db), new NoopNotifier());
        await svc.SendMessageAsync(conversation.Id, "reply");

        var msg = await db.Messages.IgnoreQueryFilters().FirstAsync();
        Assert.Null(msg.MeteredSubscriptionId);
    }
}
