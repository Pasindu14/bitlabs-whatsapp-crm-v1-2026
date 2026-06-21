using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Auth;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Companies;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.ContactLists.Entities;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Features.Templates.Entities;
using wa_api.Features.Billing.Entities;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Webhooks.Entities;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null)
    : DbContext(options)
{
    // Per-request tenant; falls back to the fail-closed null object for design-time
    // (EF CLI) and other non-HTTP scopes where no ITenantContext is injected.
    private readonly ITenantContext _tenant = tenant ?? NullTenantContext.Instance;

    // Web-default (camelCase) JSON for the jsonb Components column (Plan 004 — Templates).
    private static readonly JsonSerializerOptions ComponentsJsonOptions = new(JsonSerializerDefaults.Web);

    // ── Infrastructure tables ──────────────────────────────────────────────
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();

    // ── Feature tables ─────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<WabaConnection> WabaConnections => Set<WabaConnection>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactList> ContactLists => Set<ContactList>();
    public DbSet<ContactListMember> ContactListMembers => Set<ContactListMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignContactList> CampaignContactLists => Set<CampaignContactList>();
    public DbSet<CampaignContact> CampaignContacts => Set<CampaignContact>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<Notification> Notifications => Set<Notification>();

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

        modelBuilder.Entity<WhatsAppWebhookEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.PhoneNumberId).HasMaxLength(64);
            e.Property(x => x.EventSignature).HasMaxLength(128);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.PhoneNumberId);
            // Replay dedupe: a redelivered identical payload collides on its signature. Partial index
            // (nullable) so rows without a computed signature never conflict. The inbox is platform-level
            // (written before the company is known) — NO tenant query filter, like AuditLog/IdempotencyKey.
            e.HasIndex(x => x.EventSignature, "UX_WhatsAppWebhookEvents_EventSignature")
                .IsUnique()
                .HasFilter("\"EventSignature\" IS NOT NULL");
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
            // Stripe reverse-lookup: stripeCustomerId → Company. Indexed; null for manual-only companies.
            e.Property(x => x.StripeCustomerId).HasMaxLength(100);
            e.HasIndex(x => x.StripeCustomerId).IsUnique().HasFilter("\"StripeCustomerId\" IS NOT NULL");
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
            e.Property(x => x.MessagingTier).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.QualityRating).HasMaxLength(20);
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
            e.Property(x => x.ErrorCode).HasMaxLength(32);
            e.Property(x => x.Category).HasMaxLength(32);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.ExternalMessageId);   // fast lookup by wamid for status webhooks (6.4)
            e.HasIndex(x => x.WabaConnectionId);
            e.HasIndex(x => x.ConversationId);      // thread history lookup
            e.HasIndex(x => x.CampaignId);          // campaign delivery rollup
            e.HasOne(x => x.Contact)
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.WabaConnection)
                .WithMany()
                .HasForeignKey(x => x.WabaConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Conversation)
                .WithMany()
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);   // conversations are soft-deleted, never hard-removed
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.LastMessageBody).HasMaxLength(512);
            e.Property(x => x.LastMessageDirection).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => new { x.CompanyId, x.LastMessageAt });   // inbox sort within a tenant

            // One OPEN conversation per (company, contact, WABA number). Partial-unique mirrors the
            // Subscriptions "active" index: Status is stored as text, so the filter is on 'Open'.
            // Closed threads (a future feature) are excluded, so a contact can keep history plus one
            // live thread. FindOrCreate tolerates a concurrent insert on this index (catch + re-query).
            e.HasIndex(x => new { x.CompanyId, x.ContactId, x.WabaConnectionId }, "UX_Conversations_OpenThread")
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");

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

            // Tenant isolation (Phase 1.1/1.3): SuperAdmin bypasses; everyone else sees only their own
            // company. The webhook writer sets CompanyId explicitly + IgnoreQueryFilters (it has no JWT).
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
            // Stripe Price ID — null for manual-only plans; required for self-service checkout.
            e.Property(x => x.StripePriceId).HasMaxLength(100);
            // Platform catalog — NO tenant query filter (shared across all companies, like Company).
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            // Stripe Subscription ID — null for manually-assigned rows. Unique when set.
            e.Property(x => x.StripeSubscriptionId).HasMaxLength(100);
            e.HasIndex(x => x.StripeSubscriptionId, "UX_Subscriptions_StripeSubscriptionId")
                .IsUnique()
                .HasFilter("\"StripeSubscriptionId\" IS NOT NULL");
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

        modelBuilder.Entity<Template>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(512);
            e.Property(x => x.Language).IsRequired().HasMaxLength(20);
            e.Property(x => x.WabaId).IsRequired().HasMaxLength(64);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ParameterFormat).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.MetaTemplateId).HasMaxLength(128);
            e.Property(x => x.RejectionReason).HasMaxLength(2048);

            // The normalized component tree is stored as jsonb. A value converter handles
            // (de)serialization; the comparer lets EF detect edits to the object graph.
            var componentsConverter = new ValueConverter<TemplateComponents, string>(
                v => JsonSerializer.Serialize(v, ComponentsJsonOptions),
                v => JsonSerializer.Deserialize<TemplateComponents>(v, ComponentsJsonOptions) ?? new TemplateComponents());
            var componentsComparer = new ValueComparer<TemplateComponents>(
                (a, b) => JsonSerializer.Serialize(a, ComponentsJsonOptions) == JsonSerializer.Serialize(b, ComponentsJsonOptions),
                v => v == null ? 0 : JsonSerializer.Serialize(v, ComponentsJsonOptions).GetHashCode(),
                v => JsonSerializer.Deserialize<TemplateComponents>(JsonSerializer.Serialize(v, ComponentsJsonOptions), ComponentsJsonOptions)!);
            e.Property(x => x.Components)
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(componentsConverter, componentsComparer);

            // Uniqueness is per-company on (Name, Language) — same name + different language is a
            // separate variant (mirrors the (CompanyId, Phone) convention on Contact).
            e.HasIndex(x => new { x.CompanyId, x.Name, x.Language }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.MetaTemplateId);   // fast lookup by Meta id for the status webhook (5.2)
            e.HasOne(x => x.WabaConnection)
                .WithMany()
                .HasForeignKey(x => x.WabaConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant isolation (Phase 1.1/1.3): SuperAdmin bypasses; everyone else sees only their
            // own company. IsActive is intentionally left out (mirrors WabaConnection).
            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Campaign>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ScheduleType).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.VariableMapping).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.RecurrenceCron).HasMaxLength(120);
            e.Property(x => x.HangfireJobId).HasMaxLength(128);
            // ContactListId kept as nullable legacy column; use CampaignContactLists for actual targeting.
            e.Property(x => x.ContactListId).IsRequired(false);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ScheduledAt);
            e.HasOne(x => x.Template)
                .WithMany()
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ContactList)
                .WithMany()
                .HasForeignKey(x => x.ContactListId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.ContactLists)
                .WithOne(x => x.Campaign)
                .HasForeignKey(x => x.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.IndividualContacts)
                .WithOne(x => x.Campaign)
                .HasForeignKey(x => x.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<CampaignContactList>(e =>
        {
            e.HasKey(x => x.Id);
            // One row per (campaign, list) — adding the same list twice is a no-op.
            e.HasIndex(x => new { x.CampaignId, x.ContactListId }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasOne(x => x.ContactList)
                .WithMany()
                .HasForeignKey(x => x.ContactListId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<CampaignContact>(e =>
        {
            e.HasKey(x => x.Id);
            // One row per (campaign, contact) — adding the same contact twice is a no-op.
            e.HasIndex(x => new { x.CampaignId, x.ContactId }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasOne(x => x.Contact)
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<CampaignRecipient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.ResolvedVariables).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(256);
            // One row per (campaign, contact) — re-sending to the same contact is idempotent.
            e.HasIndex(x => new { x.CampaignId, x.ContactId }).IsUnique();
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.MessageId);
            e.HasOne(x => x.Campaign)
                .WithMany(c => c.Recipients)
                .HasForeignKey(x => x.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Contact)
                .WithMany()
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Message)
                .WithMany()
                .HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Body).IsRequired().HasMaxLength(1000);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => new { x.CompanyId, x.IsRead, x.CreatedAt });
            // One warning per type per campaign — makes the checker job idempotent.
            e.HasIndex(x => new { x.CampaignId, x.Type }, "UX_Notifications_CampaignId_Type")
                .IsUnique()
                .HasFilter("\"CampaignId\" IS NOT NULL");
            // One approval per type per template — makes the sync job idempotent.
            e.HasIndex(x => new { x.TemplateId, x.Type }, "UX_Notifications_TemplateId_Type")
                .IsUnique()
                .HasFilter("\"TemplateId\" IS NOT NULL");
            e.HasOne(x => x.Campaign)
                .WithMany()
                .HasForeignKey(x => x.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Template)
                .WithMany()
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<wa_api.Features.Companies.Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.HasKey(x => x.Id);
            // StripeInvoiceId is the idempotency key — replayed invoice.paid events collide here.
            e.Property(x => x.StripeInvoiceId).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.StripeInvoiceId).IsUnique();
            e.Property(x => x.StripeSubscriptionId).HasMaxLength(100);
            e.HasIndex(x => x.StripeSubscriptionId);
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.Property(x => x.HostedInvoiceUrl).HasMaxLength(2048);
            e.Property(x => x.InvoicePdfUrl).HasMaxLength(2048);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => new { x.CompanyId, x.PaidAt });
            e.HasOne<Features.Companies.Company>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId);
        });
    }
}
