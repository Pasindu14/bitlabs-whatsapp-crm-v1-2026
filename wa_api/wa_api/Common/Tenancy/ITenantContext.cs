namespace wa_api.Common.Tenancy;

/// <summary>
/// Per-request tenant identity, resolved server-side from JWT claims (never from the client body).
/// Drives the EF Core global query filter on <see cref="ITenantEntity"/> and the
/// CompanyId auto-stamp in <c>AuditInterceptor</c>.
/// <para>
/// <see cref="CompanyId"/> is null for SuperAdmin (platform plane) and for non-HTTP scopes
/// (migrations, seeding, background jobs). <see cref="IsSuperAdmin"/> grants the cross-tenant
/// bypass — see <c>AppDbContext.OnModelCreating</c>.
/// </para>
/// </summary>
public interface ITenantContext
{
    /// <summary>Owning company for the current request; null for SuperAdmin / non-HTTP scopes.</summary>
    Guid? CompanyId { get; }

    /// <summary>True when the caller is the platform SuperAdmin — bypasses tenant scoping.</summary>
    bool IsSuperAdmin { get; }
}
