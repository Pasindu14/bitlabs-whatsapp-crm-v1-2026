using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Json;
using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Common.Audit;

/// <summary>
/// On every SaveChanges: (1) stamps <see cref="BaseEntity"/> timestamps, and
/// (2) writes an <see cref="AuditLog"/> trail for each create/update/delete.
/// <para>
/// Phase 1.1 will extend <see cref="StampEntities"/> to auto-set <c>CompanyId</c>
/// on insert from <c>ITenantContext</c> (see the marked extension point).
/// </para>
/// </summary>
public class AuditInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    private static readonly string[] _excluded =
        ["Password", "PasswordHash", "RefreshToken", "Pin", "Token", "AccessToken", "Secret"];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        // One timestamp for the whole SaveChanges so entity stamps and their audit rows share an instant.
        var now = DateTime.UtcNow;
        StampEntities(context, now);
        AddAuditLogs(context, now);
    }

    private void StampEntities(DbContext context, DateTime now)
    {
        // Resolved per-request from the same scope as the DbContext (null outside an HTTP
        // request — migrations, seeding, jobs — in which case nothing is auto-stamped).
        var tenant = _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    // Phase 1.1: auto-stamp the tenant on insert when the caller didn't set it
                    // explicitly. SuperAdmin-driven inserts (which choose CompanyId) keep theirs.
                    if (entry.Entity is ITenantEntity te && te.CompanyId == Guid.Empty
                        && tenant?.CompanyId is { } companyId)
                        te.CompanyId = companyId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    break;
            }
        }
    }

    private void AddAuditLogs(DbContext context, DateTime now)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var correlationId = httpContext?.Items["CorrelationId"]?.ToString();
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
        var changedBy = httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var auditEntries = entries.Select(e => new AuditLog
        {
            EntityType = e.Entity.GetType().Name,
            EntityId = e.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString() ?? string.Empty,
            Operation = e.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted => "DELETE",
                _ => "UNKNOWN"
            },
            OldValues = e.State == EntityState.Added ? null : Serialize(e, original: true),
            NewValues = e.State == EntityState.Deleted ? null : Serialize(e, original: false),
            ChangedBy = changedBy,
            ChangedAt = now,
            CorrelationId = correlationId,
            IpAddress = ipAddress
        });

        context.Set<AuditLog>().AddRange(auditEntries);
    }

    private static string Serialize(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, bool original)
        => JsonSerializer.Serialize(
            entry.Properties
                .Where(p => !_excluded.Any(x => p.Metadata.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(p => p.Metadata.Name, p => original ? p.OriginalValue : p.CurrentValue));
}
