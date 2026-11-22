using wa_api.Common.Entities;
using wa_api.Features.Auth;

namespace wa_api.Features.Companies;

/// <summary>
/// A tenant on the platform. Created ONLY by a <see cref="UserRole.SuperAdmin"/>;
/// every tenant-scoped user (CompanyAdmin / Agent) belongs to exactly one company
/// via <see cref="User.CompanyId"/>.
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>.
/// </para>
/// </summary>
public class Company : BaseEntity
{
    /// <summary>Display name. Unique platform-wide.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional URL-friendly identifier. Unique when set.</summary>
    public string? Slug { get; set; }

    /// <summary>Primary contact email for the company.</summary>
    public string? Email { get; set; }

    /// <summary>Primary contact phone for the company.</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// IANA timezone the company operates in — <c>Asia/Colombo</c>, <c>Europe/London</c>. Every calendar
    /// feature resolves local wall-clock rules (business hours) against this, and renders stored UTC
    /// instants back into it.
    /// <para>
    /// IANA rather than a Windows id so the value means the same thing on every host; .NET resolves both
    /// naming schemes via ICU on Windows and Linux alike. Defaults to UTC, which is always valid but
    /// almost never what a real company wants — the calendar settings screen prompts for the real one.
    /// </para>
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Stripe Customer ID (<c>cus_xxx</c>). Set on the first self-service checkout; null for
    /// companies that only ever use the manual SuperAdmin assignment path.
    /// Indexed for the reverse-lookup on every Stripe webhook: <c>stripeCustomerId → Company</c>.
    /// </summary>
    public string? StripeCustomerId { get; set; }
}
