using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;
using Xunit;

namespace wa_api.Tests.Templates;

/// <summary>
/// The chatbot's sender fills body variables only, so only body-only templates may be picked for a
/// follow-up or a handoff alert. The CRM also has media-header and carousel templates (for campaigns);
/// sending one of those through the bot would be rejected by Meta.
/// </summary>
public class TemplateSendabilityTests
{
    private static TemplateComponents Body(string text = "Hi {{1}}, your slot is {{2}}.") =>
        new() { Body = new TemplateBody { Text = text } };

    [Fact]
    public void ABodyOnlyTemplate_IsSendable_WithOrWithoutAPlainHeaderFooterAndButtons()
    {
        Assert.True(TemplateSendability.IsBodyOnly(Body()));

        var dressed = Body();
        dressed.Header = new TemplateHeader { Type = "text", Text = "Reminder" };
        dressed.Footer = new TemplateFooter { Text = "Reply STOP to opt out" };
        dressed.Buttons =
        [
            new TemplateButton { Type = "quick_reply", Text = "Stop" },
            new TemplateButton { Type = "url", Text = "Website", Url = "https://example.com/book" },
            new TemplateButton { Type = "phone_number", Text = "Call", PhoneNumber = "+15550100" },
        ];
        Assert.True(TemplateSendability.IsBodyOnly(dressed));

        var noneHeader = Body();
        noneHeader.Header = new TemplateHeader { Type = "none" };
        Assert.True(TemplateSendability.IsBodyOnly(noneHeader));
    }

    [Theory]
    [InlineData("image")]
    [InlineData("VIDEO")]
    [InlineData("document")]
    public void AMediaHeader_IsNotSendable(string headerType)
    {
        var components = Body();
        components.Header = new TemplateHeader { Type = headerType, MediaHandle = "4::abc" };

        Assert.False(TemplateSendability.IsBodyOnly(components));
    }

    [Fact]
    public void AVariableInATextHeader_IsNotSendable()
    {
        var components = Body();
        components.Header = new TemplateHeader { Type = "text", Text = "Hello {{1}}" };

        Assert.False(TemplateSendability.IsBodyOnly(components));
    }

    [Fact]
    public void ADynamicButtonUrl_IsNotSendable()
    {
        var components = Body();
        components.Buttons = [new TemplateButton { Type = "url", Text = "Track", Url = "https://example.com/t/{{1}}" }];

        Assert.False(TemplateSendability.IsBodyOnly(components));
    }

    [Fact]
    public void ACarousel_IsNotSendable()
    {
        var components = Body();
        components.Carousel = new TemplateCarousel { Cards = [new TemplateCarouselCard(), new TemplateCarouselCard()] };

        Assert.False(TemplateSendability.IsBodyOnly(components));
    }

    [Fact]
    public void EveryStarterTemplate_IsSendableByTheBot()
    {
        // The starter pack exists to be used by follow-ups, so none of it may fall foul of the rule.
        Assert.All(StarterTemplates.All, s => Assert.True(TemplateSendability.IsBodyOnly(s.Components), s.Name));
    }
}
