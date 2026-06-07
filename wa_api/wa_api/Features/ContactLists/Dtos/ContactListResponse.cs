namespace wa_api.Features.ContactLists.Dtos;

/// <summary>A contact list as returned by the list/detail/create/update endpoints.</summary>
public record ContactListResponse(
    Guid Id,
    string Name,
    string? Description,
    int ContactCount,
    bool IsActive,
    DateTime CreatedAt
);
