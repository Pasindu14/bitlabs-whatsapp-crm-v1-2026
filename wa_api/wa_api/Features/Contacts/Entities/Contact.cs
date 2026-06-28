using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;

namespace wa_api.Features.Contacts.Entities;

/// <summary>
/// How a contact's opt-in consent was obtained. Recorded the first time <c>HasOptedIn</c> flips true,
/// so we can answer "why did we message this person?" — the core WhatsApp compliance question.
/// </summary>
public enum ConsentSource
{
    /// <summary>No recorded consent yet (default).</summary>
    None,
    /// <summary>Contact messaged the business first — opt-in under Meta policy (inbound webhook).</summary>
    InboundMessage,
    /// <summary>An operator set consent by hand (create/edit), attesting to off-platform proof.</summary>
    ManualEntry,
    /// <summary>Consent flag set during a bulk contact import.</summary>
    Import
}

/// <summary>
/// A person a tenant <see cref="Company"/> can message on WhatsApp. Tenant-scoped: every
/// contact belongs to exactly one company (auto-stamped from the JWT by <c>AuditInterceptor</c>)
/// and is invisible to other companies via the global query filter.
/// <para>
/// A contact lives on its own and may belong to zero, one, or many contact lists
/// (Plan 002, Step 2) through a join entity. Inherits Id, CreatedAt, UpdatedAt, IsActive
/// (soft-delete) from <see cref="BaseEntity"/>.
/// </para>
/// </summary>
public class Contact : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company. Set automatically from tenant context; never from the client.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// WhatsApp number, digits only in E.164 style without '+' (e.g. <c>94771234567</c>).
    /// Unique PER COMPANY, not globally.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True when the contact opted out (replied STOP / unsubscribe). Suppresses all outbound sends.
    /// Set automatically by InboundMessageWebhookHandler on stop-intent keywords.
    /// </summary>
    public bool IsOptedOut { get; set; } = false;

    /// <summary>UTC timestamp of the most recent opt-out request.</summary>
    public DateTime? OptedOutAt { get; set; }

    /// <summary>
    /// True when the contact has given explicit opt-in consent to receive business-initiated messages
    /// (campaigns/templates). Required by Meta policy. Defaults to false — must be explicitly set.
    /// Reset to false automatically when IsOptedOut is set.
    /// </summary>
    public bool HasOptedIn { get; set; } = false;

    /// <summary>UTC timestamp of the most recent opt-in consent grant.</summary>
    public DateTime? OptedInAt { get; set; }

    /// <summary>How this contact's opt-in was obtained. Set at each opt-in point; None until opted in.</summary>
    public ConsentSource ConsentSource { get; set; } = ConsentSource.None;

    /// <summary>
    /// True until Meta tells us this number isn't a reachable WhatsApp user. The Cloud API has no
    /// pre-send registration check, so this starts true (optimistic) and is set false the first time a
    /// send or delivery webhook returns an undeliverable-recipient code (131026). Once false, the contact
    /// is skipped in all future sends so we never waste a send — or a daily tier slot — on a dead number.
    /// </summary>
    public bool IsWhatsAppValid { get; set; } = true;

    /// <summary>UTC timestamp when this contact was first marked not on WhatsApp. Null while still valid.</summary>
    public DateTime? WhatsAppInvalidAt { get; set; }
}
