using wa_api.Features.Contacts.Entities;

namespace wa_api.Features.Contacts.Dtos;

/// <summary>A contact as returned by the list/detail/create/update endpoints.</summary>
public record ContactResponse(
    Guid Id,
    string Phone,
    string Name,
    bool IsActive,
    bool IsOptedOut,
    DateTime? OptedOutAt,
    bool HasOptedIn,
    DateTime? OptedInAt,
    /// <summary>How opt-in consent was obtained (None until opted in).</summary>
    ConsentSource ConsentSource,
    DateTime CreatedAt
);
