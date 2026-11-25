using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Messages.OutboundMedia;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Messages;

/// <summary>
/// Pins the outbound-media rules: what an agent may attach, where the file is kept, and that the 90-day
/// retention sweep removes old files (disk AND Postgres) while leaving the message — and newer files — alone.
/// </summary>
public class OutboundMediaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wa-media-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LocalMediaStorage NewStorage() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Media:StoragePath"] = _root }).Build());

    // ── Rules ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("image/jpeg", "image", 5)]
    [InlineData("image/png", "image", 5)]
    [InlineData("audio/mpeg", "audio", 16)]
    [InlineData("audio/ogg; codecs=opus", "audio", 16)]
    [InlineData("video/mp4", "video", 16)]
    [InlineData("application/pdf", "document", 100)]
    [InlineData("APPLICATION/PDF", "document", 100)]
    public void Classify_AcceptsWhatsAppTypes(string mime, string expectedType, long maxMb)
    {
        var kind = OutboundMediaRules.Classify(mime);

        Assert.NotNull(kind);
        Assert.Equal(expectedType, kind!.MediaType);
        Assert.Equal(maxMb * 1024 * 1024, kind.MaxBytes);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("audio/webm")]          // what a browser records — Meta won't take it
    [InlineData("application/zip")]
    [InlineData("application/x-msdownload")]
    [InlineData("text/html")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_RejectsEverythingElse(string? mime) => Assert.Null(OutboundMediaRules.Classify(mime));

    [Fact] // The stored extension comes from the MIME table, never from a user-supplied file name.
    public void Extension_ComesFromMime_NotFromFileName()
        => Assert.Equal(".pdf", OutboundMediaRules.Classify("application/pdf")!.Extension);

    [Fact]
    public void Audio_HasNoCaption() => Assert.False(OutboundMediaRules.SupportsCaption("audio"));

    // ── Storage ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Storage_RoundTrips_AndDeletes()
    {
        var storage = NewStorage();
        var bytes = new byte[] { 1, 2, 3, 4 };

        var path = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), ".jpg", bytes);

        Assert.True(storage.Exists(path));
        await using (var s = storage.OpenRead(path))
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            Assert.Equal(bytes, ms.ToArray());
        }

        Assert.True(storage.Delete(path));
        Assert.False(storage.Exists(path));
        Assert.False(storage.Delete(path));   // already gone is not an error
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("a/../../outside.txt")]
    public void Storage_RefusesPathsOutsideTheRoot(string path)
    {
        var storage = NewStorage();
        Assert.Throws<InvalidOperationException>(() => storage.Delete(path));
        Assert.Throws<InvalidOperationException>(() => storage.OpenRead(path));
    }

    // ── Retention ───────────────────────────────────────────────────────────

    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => null;
        public bool IsSuperAdmin => false;
    }

    private sealed class Fixture
    {
        public required ServiceProvider Services { get; init; }
        public required string DbName { get; init; }
    }

    private Fixture NewFixture()
    {
        var dbName = $"media-retention-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, Tenant>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        return new Fixture { Services = services.BuildServiceProvider(), DbName = dbName };
    }

    private static async Task<(Message Message, MessageMedia Media)> SeedAsync(
        AppDbContext db, string? storagePath, DateTime createdAt)
    {
        var message = new Message
        {
            Body = "[image]", MediaType = "image", MediaMimeType = "image/jpeg", MediaDownloadedAt = createdAt,
        };
        var media = new MessageMedia
        {
            MessageId = message.Id, ContentType = "image/jpeg", Data = storagePath is null ? [9, 9] : [],
            StoragePath = storagePath, CreatedAt = createdAt,
        };
        db.Messages.Add(message);
        db.MessageMedia.Add(media);
        await db.SaveChangesAsync();
        // The in-memory provider has no audit interceptor, but be explicit about the age we want.
        media.CreatedAt = createdAt;
        await db.SaveChangesAsync();
        return (message, media);
    }

    private MediaRetentionJob NewJob(Fixture f, IMediaStorage storage, int? days = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(days is null ? [] : new Dictionary<string, string?> { ["Media:RetentionDays"] = days.ToString() })
            .Build();
        return new MediaRetentionJob(
            f.Services.GetRequiredService<IServiceScopeFactory>(), storage, config, NullLogger<MediaRetentionJob>.Instance);
    }

    [Fact]
    public async Task Retention_RemovesOldFilesAndRows_KeepsMessageAndNewerFiles()
    {
        var f = NewFixture();
        var storage = NewStorage();
        var oldPath = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), ".jpg", [1]);
        var newPath = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), ".jpg", [2]);

        Guid oldMsg, oldInboundMsg, newMsg;
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            oldMsg = (await SeedAsync(db, oldPath, DateTime.UtcNow.AddDays(-91))).Message.Id;          // sent, expired
            oldInboundMsg = (await SeedAsync(db, null, DateTime.UtcNow.AddDays(-120))).Message.Id;       // received, expired
            newMsg = (await SeedAsync(db, newPath, DateTime.UtcNow.AddDays(-10))).Message.Id;            // fresh
        }

        await NewJob(f, storage).RunAsync();

        await using var verify = f.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(storage.Exists(oldPath));                 // file gone from disk
        Assert.True(storage.Exists(newPath));                  // recent file untouched

        Assert.Single(vdb.MessageMedia.IgnoreQueryFilters());  // only the fresh row remains
        Assert.Equal(newMsg, vdb.MessageMedia.IgnoreQueryFilters().Single().MessageId);

        var messages = await vdb.Messages.IgnoreQueryFilters().ToDictionaryAsync(m => m.Id);
        Assert.NotNull(messages[oldMsg].MediaPurgedAt);        // message kept, flagged expired
        Assert.NotNull(messages[oldInboundMsg].MediaPurgedAt);
        Assert.Null(messages[newMsg].MediaPurgedAt);
    }

    [Fact] // A missing file (already cleaned by hand) must not stop the sweep.
    public async Task Retention_ToleratesAMissingFile()
    {
        var f = NewFixture();
        var storage = NewStorage();

        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedAsync(db, "gone/already.jpg", DateTime.UtcNow.AddDays(-200));
        }

        await NewJob(f, storage).RunAsync();

        await using var verify = f.Services.CreateAsyncScope();
        Assert.Empty(verify.ServiceProvider.GetRequiredService<AppDbContext>().MessageMedia.IgnoreQueryFilters());
    }

    [Fact] // Setting RetentionDays to 0 keeps everything.
    public async Task Retention_Disabled_KeepsEverything()
    {
        var f = NewFixture();
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedAsync(db, null, DateTime.UtcNow.AddDays(-500));
        }

        await NewJob(f, NewStorage(), days: 0).RunAsync();

        await using var verify = f.Services.CreateAsyncScope();
        Assert.Single(verify.ServiceProvider.GetRequiredService<AppDbContext>().MessageMedia.IgnoreQueryFilters());
    }
}
