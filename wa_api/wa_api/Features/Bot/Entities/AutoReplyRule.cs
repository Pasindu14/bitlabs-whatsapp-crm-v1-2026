using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Bot.Entities;

/// <summary>
/// How a rule decides whether an inbound message is a match. Stored as text (like every other enum
/// here) so appending values never needs a migration of existing rows.
/// </summary>
public enum AutoReplyTrigger
{
    /// <summary>Any trigger term appears as a whole word in the message.</summary>
    Keyword,

    /// <summary>Any trigger term appears anywhere in the message, including inside a longer word.</summary>
    Contains,

    /// <summary>The whole (trimmed) message equals a trigger term.</summary>
    Exact,

    /// <summary>Any trigger term is a .NET regular expression that matches the message.</summary>
    Regex,

    /// <summary>Matches only the customer's FIRST message in a conversation. Trigger terms are ignored.</summary>
    Welcome,

    /// <summary>Matches anything. Trigger terms are ignored — the catch-all when nothing else fits.</summary>
    Fallback,
}

/// <summary>
/// One auto-reply rule: a trigger condition plus the text to send back. Tenant-scoped via
/// <see cref="CompanyId"/> and isolated by the global query filter, exactly like every other
/// tenant entity.
/// <para>
/// Rules are evaluated in <see cref="Priority"/> order (ascending — 0 first) and the FIRST match
/// wins, so a <see cref="AutoReplyTrigger.Fallback"/> rule belongs at a high priority number.
/// Matching itself lives in <c>AutoReplyEngine</c> and touches no I/O.
/// </para>
/// </summary>
public class AutoReplyRule : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Set from tenant context on the HTTP path, or explicitly in the
    /// background job (jobs have no tenant context — never relies on auto-stamping there).</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Operator-facing label. Unique per company.</summary>
    public string Name { get; set; } = string.Empty;

    public AutoReplyTrigger Trigger { get; set; } = AutoReplyTrigger.Keyword;

    /// <summary>
    /// Trigger terms, matched case-insensitively. Ignored by <see cref="AutoReplyTrigger.Welcome"/>
    /// and <see cref="AutoReplyTrigger.Fallback"/>, which match on position rather than content.
    /// </summary>
    public List<string> Terms { get; set; } = [];

    /// <summary>The text sent back to the customer. Plain text — same 4096-char ceiling as any message.</summary>
    public string ReplyBody { get; set; } = string.Empty;

    /// <summary>Evaluation order, ascending. Ties break on <see cref="BaseEntity.CreatedAt"/>.</summary>
    public int Priority { get; set; }

    /// <summary>Lets an operator park a rule without deleting it. Distinct from <see cref="BaseEntity.IsActive"/>,
    /// which is the platform-wide soft-delete flag.</summary>
    public bool IsEnabled { get; set; } = true;
}
