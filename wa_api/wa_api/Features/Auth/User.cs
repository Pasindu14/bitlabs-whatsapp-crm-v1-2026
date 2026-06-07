using wa_api.Common.Entities;
using wa_api.Features.Companies;

namespace wa_api.Features.Auth;

/// <summary>
/// Platform user. Authenticated via email + password (single sign-in for every role).
/// <para>
/// <see cref="CompanyId"/> is null ONLY for <see cref="UserRole.SuperAdmin"/>;
/// every other role is tenant-scoped and must carry a CompanyId.
/// </para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>.
/// </summary>
public class User : BaseEntity
{
    /// <summary>Login identity. Unique platform-wide.</summary>
    public string Email { get; set; } = null!;

    /// <summary>BCrypt hash — never the raw password.</summary>
    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public UserRole Role { get; set; }

    /// <summary>
    /// Fine-grained capability grants from the <see cref="Permission"/> catalog. Meaningful
    /// only for <see cref="UserRole.Agent"/> users — SuperAdmin/CompanyAdmin are all-access by
    /// role and keep this empty. Stored as a Postgres <c>text[]</c> column.
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>Null for SuperAdmin; required for every tenant-scoped role.</summary>
    public Guid? CompanyId { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>Owning tenant. Null for SuperAdmin; set for every tenant-scoped role.</summary>
    public Company? Company { get; set; }
}
