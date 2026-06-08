namespace wa_api.Common.Tenancy;

/// <summary>
/// Marks an entity as tenant-scoped: it belongs to exactly one <c>Company</c> via
/// <see cref="CompanyId"/>. SuperAdmin rows (e.g. platform users) are NOT tenant entities.
/// <para>
/// Phase 1.1 extension point (see <c>AuditInterceptor.StampEntities</c>): on insert,
/// <see cref="CompanyId"/> will be auto-stamped from the current <c>ITenantContext</c>.
/// </para>
/// </summary>
public interface ITenantEntity
{
    Guid CompanyId { get; set; }
}
