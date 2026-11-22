using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using Xunit;

namespace wa_api.Tests.Bot;

public class BookingConfirmationTests
{
    private static readonly DateTime TenNovAtFive = new(2026, 11, 10, 17, 0, 0);

    [Theory]
    [InlineData("Just to confirm: PRP for Pasindu on Tue 10 Nov, 5:00 PM. Shall I book it?")]
    [InlineData("Tuesday 10 Nov at 5pm, under Pasindu. Shall I confirm it?")]
    [InlineData("So that's the 10th at 5 p.m., right?")]
    [InlineData("10/11 at 17:00 for you, ok?")]
    public void TheDayAndTimeShown_CountsAsShown(string botText) =>
        Assert.True(BookingConfirmation.Shows(botText, TenNovAtFive));

    [Theory]
    [InlineData("We have 3:00 PM or 4:00 PM free on Tue 10 Nov.")]   // the day, but not this time
    [InlineData("Would 5:00 PM on Wed 11 Nov work?")]                 // the time, but another day
    [InlineData("Morning or afternoon?")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_DoesNot(string? botText) =>
        Assert.False(BookingConfirmation.Shows(botText, TenNovAtFive));

    [Fact]
    public void TheDayNumberInsideATime_IsNotTheDay()
    {
        // "5:00" must not count as "the 5th".
        Assert.False(BookingConfirmation.Shows("See you at 5:00 PM", new DateTime(2026, 11, 5, 17, 0, 0)));
    }

    [Fact]
    public void TheBotsMessageBeforeTheCustomersReply_IsRead_AllItsBubbles()
    {
        var text = BookingConfirmation.LastBotMessageBeforeReply(
        [
            new AiTranscriptTurn(true, "hi"),
            new AiTranscriptTurn(false, "Just to confirm: Tue 10 Nov,"),
            new AiTranscriptTurn(false, "5:00 PM. Shall I book it?"),
            new AiTranscriptTurn(true, "yes"),
            new AiTranscriptTurn(true, "please"),
        ]);

        Assert.Equal("Just to confirm: Tue 10 Nov,\n5:00 PM. Shall I book it?", text);
    }

    [Fact]
    public void NoReplySinceTheBotSpoke_IsNull() =>
        Assert.Null(BookingConfirmation.LastBotMessageBeforeReply(
            [new AiTranscriptTurn(true, "hi"), new AiTranscriptTurn(false, "Shall I book it?")]));
}
