namespace wa_api.Features.Templates.Entities;

/// <summary>
/// Our NORMALIZED component tree — the easy-to-render/edit shape stored in the <c>jsonb</c>
/// <c>Components</c> column. <c>TemplateMapper</c> (Plan 004 Step 2) turns this into Meta's
/// <c>components</c> array + <c>example</c> objects on submit. Casing is irrelevant on the wire:
/// the API serializes camelCase, and storage uses the same options.
/// </summary>
public class TemplateComponents
{
    public TemplateHeader? Header { get; set; }
    public TemplateBody Body { get; set; } = new();
    public TemplateFooter? Footer { get; set; }
    public List<TemplateButton> Buttons { get; set; } = [];

    /// <summary>Optional carousel (Plan 004 Step 6). Null for ordinary templates.</summary>
    public TemplateCarousel? Carousel { get; set; }
}

/// <summary>Optional header. <see cref="Type"/> is one of: none | text | image | video | document | location.</summary>
public class TemplateHeader
{
    public string Type { get; set; } = "none";

    /// <summary>Header text (Type = text). May contain a single <c>{{1}}</c> variable.</summary>
    public string? Text { get; set; }

    /// <summary>Example value for the header's <c>{{1}}</c> (Type = text with a variable).</summary>
    public string? TextExample { get; set; }

    /// <summary>Sample-media handle from the resumable upload (Type = image/video/document) → Meta <c>header_handle</c>.</summary>
    public string? MediaHandle { get; set; }
}

/// <summary>Mandatory body. <see cref="Examples"/> holds one sample per positional <c>{{n}}</c>.</summary>
public class TemplateBody
{
    public string Text { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = [];
}

/// <summary>Optional footer (plain text, no variables).</summary>
public class TemplateFooter
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>A button. <see cref="Type"/> is one of: quick_reply | url | phone_number | copy_code.</summary>
public class TemplateButton
{
    public string Type { get; set; } = "quick_reply";
    public string Text { get; set; } = string.Empty;

    /// <summary>Destination for url buttons. May end in a dynamic <c>{{1}}</c> suffix.</summary>
    public string? Url { get; set; }

    /// <summary>Example for the dynamic url suffix (when <see cref="Url"/> contains <c>{{1}}</c>).</summary>
    public string? UrlExample { get; set; }

    /// <summary>Phone number for phone_number buttons (E.164).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Example coupon code for copy_code buttons.</summary>
    public string? Example { get; set; }
}

/// <summary>Carousel wrapper (Plan 004 Step 6).</summary>
public class TemplateCarousel
{
    public List<TemplateCarouselCard> Cards { get; set; } = [];
}

/// <summary>One carousel card: a media header + body + up to two buttons.</summary>
public class TemplateCarouselCard
{
    public TemplateHeader? Header { get; set; }
    public TemplateBody Body { get; set; } = new();
    public List<TemplateButton> Buttons { get; set; } = [];
}
