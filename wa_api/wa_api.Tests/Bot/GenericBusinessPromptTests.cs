using System.Text.RegularExpressions;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.Templates;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Plan 015: the built-in prompt is for ANY business. One client's trade words once reached every
/// company's bot, so these tests fail the build if clinic wording ever comes back into shared text.
/// </summary>
public class GenericBusinessPromptTests
{
    // Words that belong to one trade. A business that is not a clinic must never be told them.
    private static readonly Regex TradeWords = new(
        @"\b(clinic|clinics|treatment|treatments|medic\w*|patient|patients|doctor|symptom\w*|practise medicine|PRP)\b",
        RegexOptions.IgnoreCase);

    private static AiBusinessContext BikeShop(bool bookingsEnabled = true, string? industryRules = null) => new(
        BusinessName: "Kandy Cycles",
        BusinessDescription: "We sell and repair bicycles in Kandy.",
        ToneNotes: "Friendly and short.",
        Location: "45 Peradeniya Road, Kandy.",
        ContactNote: "081 222 3344",
        BookingInstructions: bookingsEnabled ? null : "Ask them to visit the shop or call to order.",
        PreferredLanguage: "English",
        CurrencyLabel: "Rs.",
        Services: [new AiServiceInfo(Guid.NewGuid(), "Full service", "Brakes, gears and a wash.", 60, 4500m)],
        Faqs: [new AiFaqInfo("Do you deliver?", "Yes, anywhere in Kandy for Rs. 500.")],
        OpeningHours: [new AiOpeningHoursLine("Monday", "09:00 - 18:00")],
        Links: [new AiLinkInfo("Price list", "When they ask for prices")],
        IndustryRules: industryRules,
        BookingsEnabled: bookingsEnabled);

    private static AiTurnContext Turn(AiBusinessContext business, bool followUp = false, int asks = 0) => new(
        Business: business,
        TimeZoneId: "Asia/Colombo",
        NowLocal: new DateTime(2026, 11, 6, 10, 0, 0),
        ContactFirstName: null,
        Transcript: [],
        AsksMade: asks,
        IsFollowUp: followUp,
        Bookings:
        [
            new AiBookingInfo("Full service", new DateTime(2026, 10, 1, 10, 0, 0), AppointmentStatus.Completed, false),
        ]);

    private static void AssertNoTradeWords(string text)
    {
        var match = TradeWords.Match(text);
        Assert.False(match.Success, $"Found trade word \"{match.Value}\" in: …{Around(text, match.Index)}…");
    }

    private static string Around(string text, int at) =>
        text.Substring(Math.Max(0, at - 60), Math.Min(120, text.Length - Math.Max(0, at - 60)));

    // ── No clinic words in shared text ────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StablePrompt_HasNoTradeWords_WithoutIndustryRules(bool bookingsEnabled)
    {
        AssertNoTradeWords(AiPromptBuilder.BuildStableSystem(BikeShop(bookingsEnabled)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void VolatilePrompt_HasNoTradeWords(bool bookingsEnabled, bool followUp)
    {
        AssertNoTradeWords(AiPromptBuilder.BuildVolatileSystem(Turn(BikeShop(bookingsEnabled), followUp)));
    }

    [Fact]
    public void GateAndHoldingMessage_HaveNoTradeWords()
    {
        AssertNoTradeWords(AiReplyPlanner.GateInstruction);
        AssertNoTradeWords(AiReplyPlanner.HoldingMessage);
    }

    [Fact]
    public void ToolDescriptions_HaveNoTradeWords()
    {
        foreach (var tool in BotTools.Definitions) AssertNoTradeWords(tool.Description ?? string.Empty);
    }

    [Fact]
    public void StarterTemplates_HaveNoTradeWordsOrAed()
    {
        foreach (var t in StarterTemplates.All)
        {
            var text = string.Join(" | ",
                [t.Description, t.Components.Body.Text, .. t.Components.Body.Examples, .. t.VariableHints]);
            AssertNoTradeWords(text);
            Assert.DoesNotContain("AED", text);
            Assert.DoesNotContain("Tabby", text);
        }
    }

    // ── Industry rules ────────────────────────────────────────────────────────

    [Fact]
    public void IndustryRules_AreRenderedVerbatim_AsBindingRules()
    {
        var prompt = AiPromptBuilder.BuildStableSystem(BikeShop(industryRules: IndustryRulePresets.Healthcare));

        Assert.Contains("RULES FOR THIS BUSINESS (from the owner, as binding as the rules above)", prompt);
        Assert.Contains(IndustryRulePresets.Healthcare, prompt);
        Assert.True(
            prompt.IndexOf("RULES FOR THIS BUSINESS", StringComparison.Ordinal)
            < prompt.IndexOf("YOUR TOOLS", StringComparison.Ordinal),
            "Industry rules must sit with the rules, before the tools.");
    }

    [Fact]
    public void NoIndustryRules_LeavesTheSectionOut()
    {
        Assert.DoesNotContain("RULES FOR THIS BUSINESS", AiPromptBuilder.BuildStableSystem(BikeShop()));
    }

    // ── Bookings off ──────────────────────────────────────────────────────────

    [Fact]
    public void BookingsOff_LeavesOutEveryBookingToolAndRule()
    {
        var prompt = AiPromptBuilder.BuildStableSystem(BikeShop(bookingsEnabled: false));

        foreach (var tool in new[]
                 {
                     BotTools.CheckAvailability, BotTools.BookAppointment, BotTools.FindAppointments,
                     BotTools.CancelAppointment, BotTools.RescheduleAppointment,
                 })
        {
            Assert.DoesNotContain(tool, prompt);
        }

        Assert.DoesNotContain("SERVICES YOU CAN BOOK", prompt);
        Assert.DoesNotContain("turn the conversation into a booking", prompt);
        Assert.Contains("SERVICES AND PRICES", prompt);
        Assert.Contains("NEXT STEP FROM THE OWNER", prompt);
        Assert.Contains("Ask them to visit the shop or call to order.", prompt);
        Assert.Contains("Never say", prompt);
        Assert.Contains("handoff_to_human", prompt);
        Assert.Contains("share_link", prompt);
    }

    [Fact]
    public void BookingsOn_KeepsTheBookingFlow()
    {
        var prompt = AiPromptBuilder.BuildStableSystem(BikeShop(bookingsEnabled: true));

        Assert.Contains("SERVICES YOU CAN BOOK", prompt);
        Assert.Contains("check_availability", prompt);
        Assert.Contains("book_appointment", prompt);
        Assert.DoesNotContain("NEXT STEP FROM THE OWNER", prompt);
    }

    [Fact]
    public void BookingsOff_VolatileSkipsBookingsAndAskCounts()
    {
        var text = AiPromptBuilder.BuildVolatileSystem(Turn(BikeShop(bookingsEnabled: false), asks: 2));

        Assert.DoesNotContain("THEIR BOOKINGS", text);
        Assert.DoesNotContain("brought up booking", text);
        Assert.DoesNotContain("before booking", text);
    }

    [Fact]
    public void BookingsOff_SendsOnlyHandoffAndShareLink()
    {
        var names = BotTools.DefinitionsFor(false).Select(t => t.Name).ToList();

        Assert.Equal([BotTools.HandoffToHuman, BotTools.ShareLink], names);
        Assert.Equal(2, BotTools.For(false).Count);
        Assert.Same(BotTools.All, BotTools.For(true));
    }

    [Theory]
    [InlineData("book_appointment", false, false)]
    [InlineData("check_availability", false, false)]
    [InlineData("cancel_appointment", false, false)]
    [InlineData("handoff_to_human", false, true)]
    [InlineData("share_link", false, true)]
    [InlineData("book_appointment", true, true)]
    public void IsAllowed_RefusesBookingToolsWhenBookingsAreOff(string tool, bool bookingsEnabled, bool allowed)
    {
        Assert.Equal(allowed, BotTools.IsAllowed(tool, bookingsEnabled));
    }
}
