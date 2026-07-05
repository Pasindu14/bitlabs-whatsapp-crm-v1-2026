using wa_api.Features.WhatsApp.Dtos;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.WhatsApp;

public interface IWabaConnectionService
{
    /// <summary>Paged, searchable list of WABA connections (newest first by default).</summary>
    Task<(IReadOnlyList<WabaConnectionResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>Loads a single connection by id. Throws if not found.</summary>
    Task<WabaConnectionResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a connection. Throws on unknown company / duplicate phone-number id.</summary>
    Task<WabaConnectionResponse> CreateAsync(CreateWabaConnectionRequest request, CancellationToken ct = default);

    /// <summary>Updates editable fields. Token is replaced only when a new one is supplied.</summary>
    Task<WabaConnectionResponse> UpdateAsync(Guid id, UpdateWabaConnectionRequest request, CancellationToken ct = default);

    /// <summary>Marks a connection active (IsActive = true). Throws if not found.</summary>
    Task<WabaConnectionResponse> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marks a connection inactive (IsActive = false). Throws if not found.</summary>
    Task<WabaConnectionResponse> DeactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Resolves an active connection by its Meta phone-number id, ACROSS tenants — used by the webhook
    /// processor (which has no JWT) to route an event to its owning company. Bypasses the tenant query
    /// filter by design. Returns the entity (callers need CompanyId / access token); null when unknown.
    /// </summary>
    Task<WabaConnection?> GetConnectionByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default);

    /// <summary>
    /// Resolves an active connection by its Meta WABA id, ACROSS tenants — used to verify template-status
    /// webhooks, which carry no phone_number_id (only <c>entry[].id</c> = WABA id). Bypasses the tenant query
    /// filter by design. Returns the entity (callers need the App Secret); null when unknown.
    /// </summary>
    Task<WabaConnection?> GetConnectionByWabaIdAsync(string wabaId, CancellationToken ct = default);
}
