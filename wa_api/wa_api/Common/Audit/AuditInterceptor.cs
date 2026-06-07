using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using wa_api.Common.Entities;

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
        StampEntities(context);
        AddAuditLogs(context);
    }

    private static void StampEntities(DbContext context)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    // ── Phase 1.1 extension point ──────────────────────────────
                    // if (entry.Entity is ITenantEntity te && te.CompanyId == Guid.Empty)
                    //     te.CompanyId = _tenant.CompanyId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    break;
            }
        }
    }

    private void AddAuditLogs(DbContext context)
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
            ChangedAt = DateTime.UtcNow,
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
