using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Conversations.Dtos;
using wa_api.Features.Conversations.Realtime;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Messages.InboundMedia;
using wa_api.Features.Webhooks.Handlers;
using wa_api.Features.Webhooks.Payloads;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Webhooks;

/// <summary>
/// Pins the inbound-media path of <see cref="InboundMessageWebhookHandler"/>: a customer's image is recorded
/// with its media metadata (so the row knows to render an image), the bytes are NOT fetched inline (a download
/// job is enqueued for after the batch commit), and the realtime push advertises the media as not-yet-ready.
/// A plain text message must not enqueue anything. Backed by EF Core InMemory (mirrors QuotaRefundOnFailureTests).
/// </summary>
public class InboundMediaWebhookHandlerTests
{
    private sealed class Tenant : ITenantContext
    {
        // Webhook scope has no tenant context — the handler stamps CompanyId explicitly + IgnoreQueryFilters.
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => false;
    }

    /// <summary>Records the realtime pushes so the test can assert the media DTO shape.</summary>
    private sealed class RecordingNotifier : IChatNotifier
    {
        public ConversationMessageResponse? Last { get; private set; }
        public int Count { get; private set; }

        public Task MessageAsync(
            Guid companyId, ConversationResponse conversation, ConversationMessageResponse message,
            CancellationToken ct = default)
        {
            Count++;
            Last = message;
            return Task.CompletedTask;
        }

        public Task MessageDeletedAsync(
            Guid companyId, Guid conversationId, Guid messageId, ConversationResponse conversation,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Fake job client — Hangfire's Enqueue&lt;T&gt; is an extension over Create(Job, IState).</summary>
    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];
        public string Create(Job job, IState state) { Created.Add(job); return Guid.NewGuid().ToString(); }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"inbound-media-{Guid.NewGuid()}")
                .Options,
            new Tenant());

    private static WabaConnection NewConnection(Guid companyId) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        PhoneNumberId = "pn-1",
        WabaId = "waba-1",
        DisplayPhoneNumber = "+15550100",
        EncryptedAccessToken = "token",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static WebhookContext ImageMessage(WabaConnection conn, string wamid, string from, string mediaId, string? caption) => new()
    {
        Field = "messages",
        Connection = conn,
        Change = new WebhookChange
        {
            Field = "messages",
            Value = new WebhookValue
            {
                Messages =
                [
                    new WebhookInboundMessage
                    {
                        From = from,
                        Id = wamid,
                        Timestamp = "1700000000",
                        Type = "image",
                        Image = new WebhookMedia { Id = mediaId, MimeType = "image/jpeg", Caption = caption },
                    },
                ],
                Contacts = [new WebhookContact { WaId = from, Profile = new WebhookContactProfile { Name = "Mona" } }],
            },
        },
    };

    private static WebhookContext TextMessage(WabaConnection conn, string wamid, string from, string body) => new()
    {
        Field = "messages",
        Connection = conn,
        Change = new WebhookChange
        {
            Field = "messages",
            Value = new WebhookValue
            {
                Messages =
                [
                    new WebhookInboundMessage
                    {
                        From = from,
                        Id = wamid,
                        Timestamp = "1700000000",
                        Type = "text",
                        Text = new WebhookText { Body = body },
                    },
                ],
            },
        },
    };

    private static (InboundMessageWebhookHandler Handler, RecordingNotifier Notifier, RecordingJobClient Jobs) NewHandler(AppDbContext db)
    {
        var notifier = new RecordingNotifier();
        var jobs = new RecordingJobClient();
        var handler = new InboundMessageWebhookHandler(db, notifier, jobs, NullLogger<InboundMessageWebhookHandler>.Instance);
        return (handler, notifier, jobs);
    }

    [Fact]
    public async Task InboundImage_WithCaption_StoresMediaMetadata_AndEnqueuesDownloadJob()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, notifier, jobs) = NewHandler(db);
        await handler.HandleAsync(ImageMessage(conn, "wamid-img-1", "15551234567", "media-abc", "look at this"));
        await db.SaveChangesAsync(); // mirrors the dispatcher's single atomic commit

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("image", msg.MediaType);
        Assert.Equal("image/jpeg", msg.MediaMimeType);
        Assert.Equal("media-abc", msg.MetaMediaId);
        Assert.Equal("look at this", msg.Body);      // caption wins over the "[image]" placeholder
        Assert.Null(msg.MediaDownloadedAt);           // bytes not fetched inline
        Assert.Equal(MessageDirection.Inbound, msg.Direction);

        // A download job was enqueued for THIS message id (not run inline).
        var job = Assert.Single(jobs.Created);
        Assert.Equal(typeof(InboundMediaDownloadJob), job.Type);
        Assert.Equal(nameof(InboundMediaDownloadJob.RunAsync), job.Method.Name);
        Assert.Equal(msg.Id, Assert.IsType<Guid>(job.Args[0]));

        // The realtime push advertises media that isn't ready yet.
        Assert.Equal(1, notifier.Count);
        Assert.Equal("image", notifier.Last!.MediaType);
        Assert.False(notifier.Last!.MediaReady);
    }

    [Fact]
    public async Task InboundImage_NoCaption_UsesTypePlaceholderBody()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs) = NewHandler(db);
        await handler.HandleAsync(ImageMessage(conn, "wamid-img-2", "15551234567", "media-xyz", caption: null));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("[image]", msg.Body);           // preview placeholder when no caption
        var convo = await db.Conversations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("[image]", convo.LastMessageBody);
        Assert.Single(jobs.Created);
    }

    [Fact]
    public async Task InboundText_StoresNoMedia_AndEnqueuesNothing()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb();
        var conn = NewConnection(companyId);
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync();

        var (handler, _, jobs) = NewHandler(db);
        await handler.HandleAsync(TextMessage(conn, "wamid-txt-1", "15551234567", "hello there"));
        await db.SaveChangesAsync();

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        Assert.Null(msg.MediaType);
        Assert.Null(msg.MetaMediaId);
        Assert.Equal("hello there", msg.Body);
        Assert.Empty(jobs.Created);                   // no media → no download job
    }
}
