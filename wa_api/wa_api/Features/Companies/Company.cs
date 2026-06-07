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
}
