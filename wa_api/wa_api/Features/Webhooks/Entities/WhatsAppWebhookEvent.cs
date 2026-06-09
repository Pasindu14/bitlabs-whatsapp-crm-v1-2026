using wa_api.Common.Entities;

namespace wa_api.Features.Webhooks.Entities;

/// <summary>Lifecycle of an inbound webhook row (the durable inbox doubles as the dead-letter queue).</summary>
public enum WebhookEventStatus { Received, Processing, Processed, Failed, Dead }

/// <summary>
/// Transactional inbox for inbound Meta webhook deliveries (PRD 4.4). Persisted durably the moment a
/// signature-verified POST arrives — BEFORE the owning company is known — then processed asynchronously
/// by <c>WebhookProcessingJob</c>. This is the source of truth (dev Hangfire storage is in-memory and
/// lost on restart), the audit trail, the replay log, and the dead-letter queue.
/// <para>
/// NOT tenant-scoped: it does not implement <c>ITenantEntity</c> and has no query filter (like
/// <c>AuditLog</c>/<c>IdempotencyKey</c>), so it is read/written under the null-tenant background scope
/// with <c>IgnoreQueryFilters()</c>. Inherits Id/CreatedAt/UpdatedAt/IsActive from <see cref="BaseEntity"/>.
/// </para>
/// </summary>
public class WhatsAppWebhookEvent : BaseEntity
{
    /// <summary>UTC at ingest (distinct from <c>CreatedAt</c>, which the audit interceptor also stamps).</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>Raw request body verbatim — the exact bytes the HMAC was computed over. Stored as jsonb.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Received;

    /// <summary>Processing attempts — incremented each time the job picks the row up.</summary>
    public int Attempts { get; set; }

    /// <summary>Set when the row reaches a terminal state (Processed / Failed / Dead).</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Last error text when <see cref="Status"/> is Failed or Dead.</summary>
    public string? Error { get; set; }

    /// <summary>First <c>phone_number_id</c> seen in the payload — routing/observability only.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>SHA-256 of the raw payload — replay dedupe via a partial-unique index.</summary>
    public string? EventSignature { get; set; }
}
