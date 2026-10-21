using System.Text.Json;
using wa_api.Common.Errors;
using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;
using Xunit;

namespace wa_api.Tests.Templates;

/// <summary>
/// Pins the media-card carousel rules: what <see cref="TemplateService.Validate"/> accepts/rejects and the exact
/// Meta <c>CAROUSEL</c> component <see cref="TemplateMapper"/> produces. Meta only fails these at submit time with
/// opaque errors, so the structural rules are locked down here.
/// </summary>
public class CarouselTemplateTests
{
    private static TemplateCarouselCard Card(string type = "image", string? text = null, params TemplateButton[] buttons) => new()
    {
        Header = new TemplateHeader { Type = type, MediaHandle = "h:abc", MediaPreviewId = Guid.NewGuid() },
        Body = new TemplateBody { Text = text ?? string.Empty },
        Buttons = buttons.Length > 0 ? [.. buttons] : [new TemplateButton { Type = "url", Text = "Shop Now", Url = "https://example.com/a" }],
    };

    private static TemplateComponents Valid(int cards = 3) => new()
    {
        Body = new TemplateBody { Text = "Your outdoor space deserves a little upgrade this season." },
        Carousel = new TemplateCarousel { Cards = Enumerable.Range(0, cards).Select(_ => Card()).ToList() },
    };

    private static Dictionary<string, string[]> Errors(TemplateComponents c)
    {
        var ex = Assert.Throws<ValidationException>(() => TemplateService.Validate("spring_sale", c));
        return ex.Fields;
    }

    [Fact]
    public void ValidCarousel_Passes() => TemplateService.Validate("spring_sale", Valid());

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void CardCount_MustBeBetween2And10(int count) =>
        Assert.Contains("components.carousel.cards", Errors(Valid(count)).Keys);

    [Fact]
    public void MixedMediaTypes_AreRejected()
    {
        var c = Valid();
        c.Carousel!.Cards[1].Header!.Type = "video";
        Assert.Contains("components.carousel.cards[1].header.type", Errors(c).Keys);
    }

    [Fact]
    public void CardWithoutMedia_IsRejected()
    {
        var c = Valid();
        c.Carousel!.Cards[2].Header!.MediaHandle = null;
        Assert.Contains("components.carousel.cards[2].header.media", Errors(c).Keys);
    }

    [Fact]
    public void CardText_IsAllOrNone()
    {
        var c = Valid();
        c.Carousel!.Cards[0].Body.Text = "Tranquil Rest";
        Assert.Contains("components.carousel.cards[1].body.text", Errors(c).Keys);
    }

    [Fact]
    public void CardText_RejectsVariablesAndOverlongText()
    {
        var c = Valid(2);
        foreach (var card in c.Carousel!.Cards) card.Body.Text = "Hello {{1}}";
        Assert.Contains("components.carousel.cards[0].body.text", Errors(c).Keys);

        foreach (var card in c.Carousel.Cards) card.Body.Text = new string('a', 161);
        Assert.Contains("components.carousel.cards[0].body.text", Errors(c).Keys);
    }

    [Fact]
    public void ButtonLayout_MustMatchAcrossCards()
    {
        var c = Valid();
        c.Carousel!.Cards[1].Buttons = [new TemplateButton { Type = "quick_reply", Text = "Tell me more" }];
        Assert.Contains("components.carousel.cards[1].buttons", Errors(c).Keys);
    }

    [Fact]
    public void Cards_AllowAtMostTwoButtons_AndNoCopyCode()
    {
        var c = Valid(2);
        var three = new[]
        {
            new TemplateButton { Type = "quick_reply", Text = "A" },
            new TemplateButton { Type = "quick_reply", Text = "B" },
            new TemplateButton { Type = "quick_reply", Text = "C" },
        };
        foreach (var card in c.Carousel!.Cards) card.Buttons = [.. three];
        Assert.Contains("components.carousel.cards[0].buttons", Errors(c).Keys);

        foreach (var card in c.Carousel.Cards) card.Buttons = [new TemplateButton { Type = "copy_code", Text = "x" }];
        Assert.Contains("components.carousel.cards[0].buttons[0].type", Errors(c).Keys);
    }

    [Fact]
    public void CardUrlButtons_MustBeStatic()
    {
        var c = Valid(2);
        foreach (var card in c.Carousel!.Cards)
            card.Buttons = [new TemplateButton { Type = "url", Text = "Shop", Url = "https://example.com/{{1}}", UrlExample = "x" }];
        Assert.Contains("components.carousel.cards[0].buttons[0].url", Errors(c).Keys);
    }

    [Fact]
    public void ButtonLabel_IsCappedAt25Characters()
    {
        var c = Valid(2);
        foreach (var card in c.Carousel!.Cards)
            card.Buttons = [new TemplateButton { Type = "quick_reply", Text = new string('x', 26) }];
        Assert.Contains("components.carousel.cards[0].buttons[0].text", Errors(c).Keys);
    }

    [Fact]
    public void TopLevelHeaderFooterAndButtons_AreRejectedOnACarousel()
    {
        var c = Valid();
        c.Header = new TemplateHeader { Type = "text", Text = "Hi" };
        c.Footer = new TemplateFooter { Text = "bye" };
        c.Buttons = [new TemplateButton { Type = "quick_reply", Text = "Stop" }];
        var keys = Errors(c).Keys;
        Assert.Contains("components.header", keys);
        Assert.Contains("components.footer", keys);
        Assert.Contains("components.buttons", keys);
    }

    [Fact]
    public void Mapper_EmitsOnlyBodyAndCarousel_WithPerCardHeaderAndButtons()
    {
        var c = Valid(2);
        c.Carousel!.Cards[0].Body.Text = "Yalla Outdoors";
        c.Carousel.Cards[1].Body.Text = "Tranquil Rest";
        c.Carousel.Cards[0].Buttons = [new TemplateButton { Type = "quick_reply", Text = "More" }, new TemplateButton { Type = "url", Text = "Shop", Url = "https://e.com" }];
        c.Carousel.Cards[1].Buttons = [new TemplateButton { Type = "quick_reply", Text = "More" }, new TemplateButton { Type = "url", Text = "Shop", Url = "https://e.com" }];

        var t = new Template { Name = "spring_sale", Language = "en_US", Components = c };
        var json = JsonSerializer.Serialize(TemplateMapper.ToCreatePayload(t));
        using var doc = JsonDocument.Parse(json);

        var components = doc.RootElement.GetProperty("components");
        Assert.Equal(2, components.GetArrayLength());
        Assert.Equal("BODY", components[0].GetProperty("type").GetString());
        Assert.Equal("CAROUSEL", components[1].GetProperty("type").GetString());

        var cards = components[1].GetProperty("cards");
        Assert.Equal(2, cards.GetArrayLength());

        var card0 = cards[0].GetProperty("components");
        Assert.Equal("HEADER", card0[0].GetProperty("type").GetString());
        Assert.Equal("IMAGE", card0[0].GetProperty("format").GetString());
        Assert.Equal("h:abc", card0[0].GetProperty("example").GetProperty("header_handle")[0].GetString());
        Assert.Equal("BODY", card0[1].GetProperty("type").GetString());
        Assert.Equal("BUTTONS", card0[2].GetProperty("type").GetString());
        Assert.Equal(2, card0[2].GetProperty("buttons").GetArrayLength());
    }

    [Fact]
    public void Mapper_OmitsCardBody_WhenCardsHaveNoText()
    {
        var t = new Template { Name = "spring_sale", Language = "en_US", Components = Valid(2) };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(TemplateMapper.ToCreatePayload(t)));

        var cardComponents = doc.RootElement.GetProperty("components")[1].GetProperty("cards")[0].GetProperty("components");
        Assert.Equal(2, cardComponents.GetArrayLength()); // HEADER + BUTTONS
    }
}
