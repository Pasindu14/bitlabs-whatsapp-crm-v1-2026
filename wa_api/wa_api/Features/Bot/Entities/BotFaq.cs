using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Bot.Entities;

/// <summary>
/// One question the business is tired of answering, and the answer it wants given. Every enabled FAQ
/// for a company is injected into the system prompt verbatim.
/// <para>
/// No retrieval layer, deliberately: a small business has tens of these, not thousands, and the whole
/// set costs a few hundred tokens against a model priced at $1/MTok. Retrieval would add a failure mode
/// (the right answer not retrieved) to buy a saving that does not exist yet.
/// </para>
/// </summary>
public class BotFaq : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    /// <summary>Render order in the prompt. Ties break on <see cref="BaseEntity.CreatedAt"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>Parks an entry without deleting it, mirroring <c>AutoReplyRule.IsEnabled</c>.</summary>
    public bool IsEnabled { get; set; } = true;
}
