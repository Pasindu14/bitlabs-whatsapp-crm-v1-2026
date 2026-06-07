using Microsoft.EntityFrameworkCore;
using wa_api.Common.Audit;

namespace wa_api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ── Infrastructure tables ──────────────────────────────────────────────
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Feature DbSets are added per phase (Company → Users → ... ).

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ChangedAt);
            e.HasIndex(x => x.CorrelationId);
            e.HasIndex(x => x.CompanyId);
        });
    }
}
