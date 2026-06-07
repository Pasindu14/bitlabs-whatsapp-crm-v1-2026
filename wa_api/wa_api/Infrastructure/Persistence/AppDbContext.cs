using Microsoft.EntityFrameworkCore;
using wa_api.Common.Audit;
using wa_api.Features.Auth;
using wa_api.Features.Companies;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ── Infrastructure tables ──────────────────────────────────────────────
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // ── Feature tables ─────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<WabaConnection> WabaConnections => Set<WabaConnection>();

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
            e.HasOne(x => x.Company)                    // optional FK — SuperAdmin's CompanyId is null
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();         // company name is platform-unique
            e.Property(x => x.Slug).HasMaxLength(120);
            e.HasIndex(x => x.Slug).IsUnique();         // unique when set (nulls allowed)
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(32);
        });

        modelBuilder.Entity<WabaConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PhoneNumberId).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.PhoneNumberId).IsUnique();   // one connection per Meta phone-number id
            e.Property(x => x.WabaId).IsRequired().HasMaxLength(64);
            e.Property(x => x.DisplayPhoneNumber).HasMaxLength(32);
            e.Property(x => x.EncryptedAccessToken).IsRequired().HasMaxLength(2048);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.CompanyId);
            e.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
