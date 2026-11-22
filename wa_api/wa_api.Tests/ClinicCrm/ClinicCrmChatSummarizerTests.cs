using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Bot.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.Messages.Entities;
using wa_api.Tests.Bot;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// The handover note the clinic asked for: a few lines on concern, discussion and recommendation — built
/// from the chat, never the chat itself.
/// </summary>
public class ClinicCrmChatSummarizerTests
{
    private sealed class FakePlanner(string? answer, bool configured = true) : IAiReplyPlanner
    {
        public bool IsConfigured => configured;
        public string Model => "claude-haiku-4-5";
        public string? LastSystem { get; private set; }
        public string? LastQuestion { get; private set; }

        public Task<AiReplyPlan> PlanAsync(
            AiTurnContext ctx, IBotToolExecutor tools, int maxToolIterations, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AiAskResult> AskAsync(string system, string question, int maxTokens, CancellationToken ct = default)
        {
            LastSystem = system;
            LastQuestion = question;
            return Task.FromResult(answer is null ? AiAskResult.Unavailable : new AiAskResult(answer, new AiUsage(800, 60, 0, 0)));
        }
    }

    private static async Task SayAsync(ClinicCrmTestKit kit, params (MessageDirection Direction, string Body)[] lines)
    {
        var at = DateTime.UtcNow.AddMinutes(-lines.Length);
        foreach (var (direction, body) in lines)
        {
            kit.Db.Messages.Add(new Message
            {
                Id = Guid.NewGuid(), CompanyId = kit.CompanyId, ContactId = kit.Contact.Id,
                WabaConnectionId = kit.Conversation.WabaConnectionId, ConversationId = kit.Conversation.Id,
                Body = body, Direction = direction, Status = MessageStatus.Sent,
                CreatedAt = at = at.AddSeconds(30), IsActive = true,
            });
        }
        await kit.Db.SaveChangesAsync();
    }

    private static ClinicCrmChatSummarizer Summarizer(ClinicCrmTestKit kit, IAiReplyPlanner planner) =>
        new(kit.Db, planner, TestAiUsage.Logger(kit.Db), NullLogger<ClinicCrmChatSummarizer>.Instance);

    [Fact]
    public async Task SummarisesTheWholeChat_InOrder_AndLogsTheCost()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        await SayAsync(kit,
            (MessageDirection.Inbound, "Hi, I've had hair fall for 5 months"),
            (MessageDirection.Outbound, "PRP can help with that. 3 sessions are AED 300 on offer."),
            (MessageDirection.Inbound, "ok book me monday 9am"));
        var planner = new FakePlanner(
            "Concern: hair fall for 5 months.\nDiscussed: PRP, AED 300 for 3 sessions.\nRecommended: a PRP course.");

        var summary = await Summarizer(kit, planner).SummarizeAsync(kit.CompanyId, kit.Conversation.Id, "PRP Consultation");

        Assert.Equal("Concern: hair fall for 5 months.\nDiscussed: PRP, AED 300 for 3 sessions.\nRecommended: a PRP course.", summary);

        var question = planner.LastQuestion!;
        Assert.Contains("PRP Consultation", question);
        Assert.True(question.IndexOf("Customer: Hi, I've had hair fall", StringComparison.Ordinal)
                    < question.IndexOf("Assistant: PRP can help", StringComparison.Ordinal));
        Assert.Contains("Concern:", planner.LastSystem);

        var log = await kit.Db.AiReplyLogs.SingleAsync();
        Assert.Equal(AiCallKind.BookingSummary, log.Kind);
        Assert.Equal(kit.Conversation.Id, log.ConversationId);
    }

    [Fact]
    public async Task NothingWorthNoting_IsNoSummary()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        await SayAsync(kit, (MessageDirection.Inbound, "book me monday 9am"));

        Assert.Null(await Summarizer(kit, new FakePlanner("NONE"))
            .SummarizeAsync(kit.CompanyId, kit.Conversation.Id, "PRP Consultation"));
    }

    [Fact]
    public async Task AnUnreachableModel_OrNoKey_IsNoSummary_NotAFailure()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        await SayAsync(kit, (MessageDirection.Inbound, "I have hair fall"));

        Assert.Null(await Summarizer(kit, new FakePlanner(null))
            .SummarizeAsync(kit.CompanyId, kit.Conversation.Id, "PRP Consultation"));
        Assert.Null(await Summarizer(kit, new FakePlanner("Concern: x", configured: false))
            .SummarizeAsync(kit.CompanyId, kit.Conversation.Id, "PRP Consultation"));
    }

    [Theory]
    [InlineData("- **Concern:** hair fall\n- Discussed: PRP", "Concern: hair fall\nDiscussed: PRP")]
    [InlineData("## Summary\nConcern: -5 kg goal", "Summary\nConcern: -5 kg goal")]
    [InlineData("  \n", null)]
    [InlineData("none", null)]
    public void Clean_StripsMarkdown_ButKeepsTheWords(string raw, string? expected) =>
        Assert.Equal(expected, ClinicCrmChatSummarizer.Clean(raw));

    [Fact]
    public void Clean_CapsALongSummary()
    {
        var cleaned = ClinicCrmChatSummarizer.Clean("Concern: " + new string('x', 2000))!;
        Assert.Equal(ClinicCrmChatSummarizer.MaxSummaryLength, cleaned.Length);
        Assert.EndsWith("…", cleaned);
    }
}
