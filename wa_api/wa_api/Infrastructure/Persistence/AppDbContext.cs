using Microsoft.EntityFrameworkCore;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Auth;
using wa_api.Features.Companies;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.ContactLists.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null)
    : DbContext(options)
{
    // Per-request tenant; falls back to the fail-closed null object for design-time
    // (EF CLI) and other non-HTTP scopes where no ITenantContext is injected.
    private readonly ITenantContext _tenant = tenant ?? NullTenantContext.Instance;

    // ── Infrastructure tables ──────────────────────────────────────────────
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // ── Feature tables ─────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<WabaConnection> WabaConnections => Set<WabaConnection>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactList> ContactLists => Set<ContactList>();
    public DbSet<ContactListMember> ContactListMembers => Set<ContactListMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

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
            e.Property(x => x.Permissions)              // capability grants — Postgres text[]
                .HasColumnType("text[]")
                .IsRequired()
                .HasDefaultValueSql("'{}'::text[]");
            e.HasIndex(x => x.CompanyId);               // null for SuperAdmin; set for tenant users
            e.HasOne(x => x.Company)                    // optional FK — SuperAdmin's CompanyId is null
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).IsRequired().HasMaxLength(128);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => new { x.UserId, x.IsRevoked });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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

            // ── Phase 1.1/1.3 tenant isolation ──────────────────────────────
            // SuperAdmin bypasses the filter (cross-tenant plane); every other caller
            // sees only their own company's rows. The expression references the context
            // instance member, so EF re-evaluates it per request (set from JWT claims).
            // IsActive soft-delete is intentionally NOT folded in here so SuperAdmin
            // management pages keep seeing deactivated rows.
            e.HasQueryFilter(w => _tenant.IsSuperAdmin || w.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Phone).IsRequired().HasMaxLength(20);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            // Phone is unique PER COMPANY (composite), not globally — two companies can hold
            // the same number, one company cannot duplicate it (PRD §1.2). This composite
            // index also makes Excel re-import safe (upsert by CompanyId + Phone).
            e.HasIndex(x => new { x.CompanyId, x.Phone }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant isolation (Phase 1.1/1.3): SuperAdmin bypasses; everyone else sees only
            // their own company. IsActive is intentionally left out so deactivated contacts
            // stay visible on management pages (mirrors WabaConnection).
            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<ContactList>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(500);
            // List name is unique PER COMPANY, not globally.
            e.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<ContactListMember>(e =>
        {
            e.HasKey(x => x.Id);
            // One membership per (list, contact) — re-adding the same contact is a no-op.
            e.HasIndex(x => new { x.ContactListId, x.ContactId }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            // Hard-deleting a list or contact also clears its membership rows.
            e.HasOne<ContactList>()
                .WithMany()
                .HasForeignKey(x => x.ContactListId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Contact>()
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).IsRequired().HasMaxLength(4096);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ExternalMessageId).HasMaxLength(128);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.WabaConnectionId);
            e.HasOne(x => x.Contact)
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WabaConnection)
                .WithMany()
                .HasForeignKey(x => x.WabaConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.HasIndex(x => x.Name).IsUnique();          // plan name is platform-unique
            e.Property(x => x.Price).HasColumnType("numeric(12,2)");
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("USD");
            e.Property(x => x.FeatureFlags)               // capability grants — Postgres text[]
                .HasColumnType("text[]")
                .IsRequired()
                .HasDefaultValueSql("'{}'::text[]");
            // Platform catalog — NO tenant query filter (shared across all companies, like Company).
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.CompanyId);
            // One ACTIVE subscription per company — partial unique index. Cancelled/Inactive
            // rows are excluded, so historical subscriptions can coexist. The named overload
            // declares a SECOND, distinct index on CompanyId (a bare HasIndex(x => x.CompanyId)
            // would reuse the plain index builder above instead of adding a new one).
            e.HasIndex(x => x.CompanyId, "UX_Subscriptions_CompanyId_Active")
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'");
            e.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Plan)
                .WithMany()
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant isolation (Phase 1.1/1.3): SuperAdmin bypasses; everyone else sees only
            // their own company. IsActive is intentionally left out (mirrors WabaConnection).
            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });
    }
}
