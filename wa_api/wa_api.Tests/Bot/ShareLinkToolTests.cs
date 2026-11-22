using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Common.Tenancy;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// The share_link contract: the model can only pick a link the owner saved, by title, and never sees
/// or types the URL itself.
/// </summary>
public class ShareLinkToolTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ConversationId = Guid.NewGuid();

    private static async Task<(BotToolExecutor Executor, AppDbContext Db)> NewAsync(params BotLink[] links)
    {
        var tenant = new ScopedTenantContext(new HttpContextAccessor());
        tenant.EnterCompany(CompanyId);

        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"share-link-{Guid.NewGuid()}").Options,
            tenant);

        db.BotLinks.AddRange(links);
        await db.SaveChangesAsync();

        // share_link never touches the calendar, so the calendar services are not needed.
        var executor = new BotToolExecutor(
            db, null!, null!,
            new BotToolScope(CompanyId, Guid.NewGuid(), ConversationId, TimeZoneInfo.Utc, DryRun: false),
            NullLogger<BotToolExecutor>.Instance);

        return (executor, db);
    }

    private static BotLink Link(string title, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyId,
        Title = title,
        Url = $"https://example.com/{Guid.NewGuid():N}",
        IsEnabled = enabled,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    private static Dictionary<string, JsonElement> Args(string title) =>
        new() { ["link_title"] = JsonSerializer.SerializeToElement(title) };

    [Fact]
    public async Task AKnownTitle_ResolvesToThatLink_CaseInsensitively()
    {
        var photos = Link("Before & after photos");
        var (executor, _) = await NewAsync(photos, Link("Price list"));

        var result = await executor.ExecuteAsync(BotTools.ShareLink, Args("before & AFTER photos"), default);

        Assert.Equal(photos.Id, result.SharedLinkId);
        Assert.DoesNotContain(photos.Url, result.Content);     // the model never gets the URL
    }

    [Fact]
    public async Task AnUnknownTitle_ListsTheRealOnes_AndSharesNothing()
    {
        var (executor, _) = await NewAsync(Link("Before & after photos"), Link("Price list"));

        var result = await executor.ExecuteAsync(BotTools.ShareLink, Args("Instagram"), default);

        Assert.Null(result.SharedLinkId);
        Assert.Contains("Before & after photos", result.Content);
        Assert.Contains("Price list", result.Content);
    }

    [Fact]
    public async Task ADisabledLink_CannotBeShared()
    {
        var (executor, _) = await NewAsync(Link("Price list", enabled: false));

        var result = await executor.ExecuteAsync(BotTools.ShareLink, Args("Price list"), default);

        Assert.Null(result.SharedLinkId);
    }

    private static Message Sent(Guid conversationId, string body, MessageDirection direction = MessageDirection.Outbound) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyId,
        ConversationId = conversationId,
        Body = body,
        Direction = direction,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
    };

    [Fact]
    public async Task ALinkAlreadySentInThisConversation_IsNotSentAgain()
    {
        var photos = Link("Before and after photos");
        var (executor, db) = await NewAsync(photos);
        db.Messages.Add(Sent(ConversationId, BotLinkMessages.Compose(photos)));
        await db.SaveChangesAsync();

        var result = await executor.ExecuteAsync(BotTools.ShareLink, Args("Before and after photos"), default);

        Assert.Null(result.SharedLinkId);
        Assert.Contains("already sent", result.Content);
    }

    [Fact]
    public async Task ALinkSentInAnotherConversation_OrPastedByTheCustomer_CanStillBeShared()
    {
        var photos = Link("Before and after photos");
        var (executor, db) = await NewAsync(photos);
        db.Messages.AddRange(
            Sent(Guid.NewGuid(), BotLinkMessages.Compose(photos)),
            Sent(ConversationId, photos.Url, MessageDirection.Inbound));
        await db.SaveChangesAsync();

        var result = await executor.ExecuteAsync(BotTools.ShareLink, Args("Before and after photos"), default);

        Assert.Equal(photos.Id, result.SharedLinkId);
    }

    [Fact]
    public void ThePrompt_ListsTitles_ButNeverUrls()
    {
        var prompt = AiPromptBuilder.BuildStableSystem(new AiBusinessContext(
            "the clinic", "A hair clinic.", null, null, null, null, null, null, [], [], [],
            [new AiLinkInfo("Before & after photos", "When they ask to see results")]));

        Assert.Contains("LINKS YOU CAN SHARE", prompt);
        Assert.Contains("\"Before & after photos\" — When they ask to see results", prompt);
        Assert.DoesNotContain("https://", prompt);
    }
}
