using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// The tool list is part of the cached prompt prefix — tools render BEFORE the system blocks — so its
/// contents and its ORDER are a cache contract, not just a capability list. Reordering or
/// conditionally including a tool would silently stop every tenant's prompt from caching.
/// </summary>
public class BotToolsTests
{
    [Fact]
    public void TheToolSetIsFixedAndInAStableOrder()
    {
        var names = BotTools.All
            .Select(t => t.Value)
            .OfType<Anthropic.Models.Messages.Tool>()
            .Select(t => t.Name)
            .ToList();

        Assert.Equal(
            [
                BotTools.CheckAvailability,
                BotTools.BookAppointment,
                BotTools.FindAppointments,
                BotTools.CancelAppointment,
                BotTools.RescheduleAppointment,
                BotTools.HandoffToHuman,
                BotTools.ShareLink,
            ],
            names);
    }

    [Fact]
    public void CheckAvailability_CanBeToldTheTimeTheCustomerAskedFor()
    {
        var tool = BotTools.All
            .Select(t => t.Value)
            .OfType<Anthropic.Models.Messages.Tool>()
            .Single(t => t.Name == BotTools.CheckAvailability);

        Assert.Contains("time", tool.InputSchema.Properties!.Keys);
        Assert.DoesNotContain("time", tool.InputSchema.Required ?? []);
    }

    [Fact]
    public void TheSameInstanceIsReused()
    {
        // Rebuilding the list per request would be wasteful and, more importantly, invites someone to
        // make it conditional later — which is exactly what breaks the cache.
        Assert.Same(BotTools.All, BotTools.All);
    }

    [Fact]
    public void ThePromptTellsTheModelItCanCancelRatherThanEscalate()
    {
        // Regression: with no cancel tool, "i want to cancel my appointment" was escalated to a human,
        // which paused the bot with no auto-resume and left the thread permanently silent.
        var prompt = AiPromptBuilder.BuildStableSystem(new AiBusinessContext(
            "the clinic", "A doctor's clinic.", null, null, null, null, null, null, [], [], []));

        Assert.Contains("cancel_appointment", prompt);
        Assert.Contains("reschedule_appointment", prompt);
        Assert.Contains("never", prompt);
        Assert.Contains("hand those off", prompt);
    }
}
