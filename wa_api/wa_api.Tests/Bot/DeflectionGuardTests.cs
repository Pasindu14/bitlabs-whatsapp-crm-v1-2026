using Xunit;
using wa_api.Features.Bot.Ai;

namespace wa_api.Tests.Bot;

public class DeflectionGuardTests
{
    [Theory]
    [InlineData("HANDOFF", true)]
    [InlineData("  handoff.", true)]
    [InlineData("OK", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Verdict_OnlyHandoffTriggersAHandoff(string? verdict, bool expected) =>
        Assert.Equal(expected, AiReplyPlanner.IsHandoffVerdict(verdict));

    [Theory]
    [InlineData("HANDOFF HFC", "For HFC, our dedicated customer service specialist will assist you. Kindly wait for few moments.")]
    [InlineData("HANDOFF: \"Exosome\".", "For Exosome, our dedicated customer service specialist will assist you. Kindly wait for few moments.")]
    [InlineData("HANDOFF", AiReplyPlanner.HoldingMessage)]
    [InlineData(null, AiReplyPlanner.HoldingMessage)]
    public void HoldingMessage_NamesTheTopicWhenThereIsOne(string? verdict, string expected) =>
        Assert.Equal(expected, AiReplyPlanner.HoldingMessageFor(verdict));
}
