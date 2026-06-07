using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Companies;

namespace wa_api.Features.WhatsApp.Entities;

/// <summary>Lifecycle status of a WABA phone-number connection.</summary>
public enum WabaConnectionStatus { Connected, Disconnected, Invalid }

/// <summary>
/// A WhatsApp Business Account (WABA) phone-number connection owned by a tenant
/// <see cref="Company"/>. Managed ONLY by SuperAdmin in this phase.
/// <para>
/// Inherits Id, CreatedAt, UpdatedAt, IsActive (soft-delete) from <see cref="BaseEntity"/>;
/// is tenant-scoped via <see cref="ITenantEntity.CompanyId"/>.
/// </para>
/// </summary>
public class WabaConnection : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant company.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Meta phone-number id. Unique platform-wide.</summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>Meta WhatsApp Business Account id.</summary>
    public string WabaId { get; set; } = string.Empty;

    /// <summary>Human-readable phone number shown in the UI (e.g. +1 555 0100).</summary>
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// WhatsApp access token. Stored as-is for now; column name keeps "Encrypted"
    /// for forward-compat when at-rest encryption is added. NEVER returned to clients.
    /// </summary>
    public string EncryptedAccessToken { get; set; } = string.Empty;

    /// <summary>Connection status as reported / set by the platform.</summary>
    public WabaConnectionStatus Status { get; set; } = WabaConnectionStatus.Connected;

    /// <summary>UTC timestamp of the last Hangfire health-check poll against Meta's API.</summary>
    public DateTime? LastHealthCheckAt { get; set; }

    /// <summary>Last error message returned by Meta's API during a health check. Null when healthy.</summary>
    public string? HealthCheckErrorMessage { get; set; }

    /// <summary>Owning company navigation.</summary>
    public Company Company { get; set; } = null!;
}
