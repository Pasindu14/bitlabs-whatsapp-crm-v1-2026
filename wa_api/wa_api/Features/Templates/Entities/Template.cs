using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Templates.Entities;

/// <summary>WhatsApp template category. Fixed to <see cref="Marketing"/> in v1 (Plan 004);
/// kept as an enum so Utility/Authentication can be added later without a migration.</summary>
public enum TemplateCategory { Marketing }

/// <summary>
/// Local lifecycle of a template. <c>Draft</c> is ours (not yet sent to Meta); the rest mirror
/// Meta's <c>message_template</c> status values returned by the status poll (Plan 004 Step 3).
/// </summary>
public enum TemplateStatus { Draft, Pending, Approved, Rejected, Paused, Disabled }

/// <summary>Variable style. Pinned to <see cref="Positional"/> (<c>{{1}}</c>,<c>{{2}}</c>) for v1.</summary>
public enum TemplateParameterFormat { Positional }

/// <summary>
/// A WhatsApp message template owned by a tenant <c>Company</c>. Built in our UI, submitted to
/// Meta's <c>POST /{waba-id}/message_templates</c>, then polled for approval (Plan 004).
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>;
/// tenant-scoped via <see cref="ITenantEntity.CompanyId"/> (auto-stamped by AuditInterceptor).
/// </para>
/// </summary>
public class Template : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The connection we submit through — holds the access token + WABA id.</summary>
    public Guid WabaConnectionId { get; set; }

    /// <summary>
    /// Denormalized copy of the connection's WABA id (the WABA that owns this template at Meta).
    /// Stored on the row because <c>WabaConnection.WabaId</c> is NOT unique (two numbers can share
    /// one WABA); v1 assumes one connection per WABA (Plan 004 — WABA scoping rule).
    /// </summary>
    public string WabaId { get; set; } = string.Empty;

    /// <summary>Meta template name: lowercase letters, digits, underscores. Unique per (Company, Name, Language).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Meta language/locale code, e.g. <c>en_US</c>, <c>si_LK</c>.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Template category (Marketing in v1).</summary>
    public TemplateCategory Category { get; set; } = TemplateCategory.Marketing;

    /// <summary>Header/body/footer/buttons tree. Persisted as <c>jsonb</c>; mapped to Meta's components on submit.</summary>
    public TemplateComponents Components { get; set; } = new();

    /// <summary>Variable style (Positional in v1).</summary>
    public TemplateParameterFormat ParameterFormat { get; set; } = TemplateParameterFormat.Positional;

    /// <summary>Lifecycle status. Drives the table badge and what is editable (edit allowed only in Draft).</summary>
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;

    /// <summary>Id returned by Meta on create. Null while Draft; used to poll status + delete.</summary>
    public string? MetaTemplateId { get; set; }

    /// <summary>Reason captured from Meta when Status is Rejected.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>UTC timestamp the template was submitted to Meta.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>UTC timestamp the template was first approved by Meta. Stamped once, on the
    /// transition into <see cref="TemplateStatus.Approved"/> (never overwritten on re-approval).</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>UTC timestamp of the last Meta status poll.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>The connection used to submit. Loaded for display (phone number) and the token on submit.</summary>
    public WabaConnection WabaConnection { get; set; } = null!;
}
