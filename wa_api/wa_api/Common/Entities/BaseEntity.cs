namespace wa_api.Common.Entities;

/// <summary>
/// Root of every persisted entity. Guid PK + audit timestamps + soft-delete flag.
/// <para>
/// <see cref="IsActive"/> is the platform-wide soft-delete convention (Section 0.2 /
/// Section 3): rows are never physically removed; <c>IsActive = false</c> hides them.
/// </para>
/// <para>
/// Tenant entities additionally implement <c>ITenantEntity</c> (CompanyId) in Phase 1.1;
/// timestamps + IsActive are stamped automatically by <c>AuditInterceptor</c>.
/// </para>
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
