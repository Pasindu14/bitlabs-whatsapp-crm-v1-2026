using Microsoft.EntityFrameworkCore;
using wa_api.Common.Audit;
using wa_api.Features.Auth;

namespace wa_api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ── Infrastructure tables ──────────────────────────────────────────────
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // ── Feature tables ─────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();

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

        modelBuilder.Entity<IdempotencyKey>(e =>
        {
            e.HasKey(x => x.Key);
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();        // email is the global login identity
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.CompanyId);               // null for SuperAdmin; set for tenant users
        });
    }
}
