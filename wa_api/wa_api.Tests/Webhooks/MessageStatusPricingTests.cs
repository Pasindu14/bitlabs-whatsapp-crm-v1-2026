using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wa_api.Common.Tenancy;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Webhooks.Handlers;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Features.Webhooks.Processing;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Tests.Webhooks;

/// <summary>Plan 013: a delivery status carrying Meta's pricing puts a cost on the message, once.</summary>
public class MessageStatusPricingTests
{
    private sealed class SuperAdminTenant : ITenantContext
    {
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => true;
    }

    private sealed class NoopNotifier : IChatNotifier
    {
        public Task MessageAsync(Guid companyId, ConversationResponse conversation,
            ConversationMessageResponse message, CancellationToken ct = default) => Task.CompletedTask;
        public Task MessageDeletedAsync(Guid companyId, Guid conversationId, Guid messageId,
            ConversationResponse conversation, CancellationToken ct = default) => Task.CompletedTask;
        public Task BotStateChangedAsync(Guid companyId, ConversationResponse conversation,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task BotTypingAsync(Guid companyId, Guid conversationId, bool isTyping,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task MessageStatusAsync(Guid companyId, Guid conversationId, Guid messageId, MessageStatus status,
            DateTime? statusAt, string? errorMessage, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static MessageStatusWebhookHandler Handler(AppDbContext db, MetaPricingOptions pricing) => new(
        db,
        new NoopNotifier(),
        new WebhookPostCommitActions(NullLogger<WebhookPostCommitActions>.Instance),
        new MetaCostCalculator(Options.Create(pricing), NullLogger<MetaCostCalculator>.Instance),
        NullLogger<MessageStatusWebhookHandler>.Instance);

    private static MetaPricingOptions Rates(decimal service) => new()
    {
        Countries = new() { ["971"] = new() { Service = service, Utility = service, Marketing = 0.0499m } },
        Default = new() { Service = 0.02m, Utility = 0.02m, Marketing = 0.05m },
    };

    private static WebhookContext Status(string wamid, string status, bool billable, string category) => new()
    {
        Field = "messages",
        Change = new WebhookChange
        {
            Value = new WebhookValue
            {
                Statuses =
                [
                    new WebhookStatus
                    {
                        Id = wamid,
                        Status = status,
                        RecipientId = "971501234567",
                        Timestamp = "1790000000",
                        Pricing = new WebhookPricing { Billable = billable, Category = category, PricingModel = "PMP" },
                    },
                ],
            },
        },
    };

    [Fact]
    public async Task PricingIsSetOnce_AndALaterStatusDoesNotRepriceIt()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"status-price-{Guid.NewGuid()}").Options,
            new SuperAdminTenant());

        var message = new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ContactId = Guid.NewGuid(),
            WabaConnectionId = Guid.NewGuid(),
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Accepted,
            ExternalMessageId = "wamid.1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        await Handler(db, Rates(0.0157m)).HandleAsync(Status("wamid.1", "sent", billable: true, "service"));
        Assert.Equal(0.0157m, message.MetaCostUsd);
        Assert.Equal("service", message.Category);

        // Same message, later status, and the rate card has since changed: the stored cost stands.
        await Handler(db, Rates(0.03m)).HandleAsync(Status("wamid.1", "delivered", billable: true, "service"));
        Assert.Equal(0.0157m, message.MetaCostUsd);
    }

    [Fact]
    public async Task AMessageMetaDidNotBill_CostsNothing()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"status-free-{Guid.NewGuid()}").Options,
            new SuperAdminTenant());

        var message = new Message
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ContactId = Guid.NewGuid(),
            WabaConnectionId = Guid.NewGuid(),
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Accepted,
            ExternalMessageId = "wamid.2",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        // One of the 1,000 free service messages this month.
        await Handler(db, Rates(0.0157m)).HandleAsync(Status("wamid.2", "sent", billable: false, "service"));

        Assert.Equal(0m, message.MetaCostUsd);
    }
}
