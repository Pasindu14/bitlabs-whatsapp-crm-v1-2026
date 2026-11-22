using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Bot.Entities;

/// <summary>
/// A link the owner has approved for the assistant to share — a before/after gallery, a price list, a
/// map pin.
/// <para>
/// The model never sees <see cref="Url"/>. The prompt lists titles only, the model asks for one by
/// title through the <c>share_link</c> tool, and the reply job sends the stored URL itself as its own
/// message. A model that retypes a URL can mangle it, and nothing downstream would notice.
/// </para>
/// </summary>
public class BotLink : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>What the model asks for, e.g. "Before &amp; after photos". Unique enough to pick by name.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Absolute http(s) URL, sent verbatim.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The owner's hint for when this link fits — shown to the model next to the title.</summary>
    public string? WhenToShare { get; set; }

    /// <summary>Optional line sent above the URL. Falls back to the title when empty.</summary>
    public string? Message { get; set; }

    /// <summary>Render order in the prompt. Ties break on <see cref="BaseEntity.CreatedAt"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>Parks an entry without deleting it, mirroring <see cref="BotFaq.IsEnabled"/>.</summary>
    public bool IsEnabled { get; set; } = true;
}
