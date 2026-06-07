using wa_api.Common.Entities;

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

    /// <summary>Null for SuperAdmin; required for every tenant-scoped role.</summary>
    public Guid? CompanyId { get; set; }

    public DateTime? LastLoginAt { get; set; }

    // Navigation to Company is added in Phase 1.1 when the Company entity exists.
}
