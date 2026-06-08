namespace wa_api.Common.Audit;

/// <summary>
/// Immutable change-trail row written by <see cref="AuditInterceptor"/> for every
/// create/update/delete. Sensitive columns (passwords, tokens) are redacted before serialization.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;

    /// <summary>CREATE / UPDATE / DELETE.</summary>
    public string Operation { get; set; } = string.Empty;

    public string? OldValues { get; set; }
    public string? NewValues { get; set; }

    /// <summary>Caller user id (from JWT) when available. String to stay key-type agnostic.</summary>
    public string? ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; }

    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>Tenant that owned the change; populated once multi-tenancy lands (Phase 1.1).</summary>
    public Guid? CompanyId { get; set; }
}
