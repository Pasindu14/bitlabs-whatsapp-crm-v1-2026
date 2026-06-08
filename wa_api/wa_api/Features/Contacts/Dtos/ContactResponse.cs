namespace wa_api.Features.Contacts.Dtos;

/// <summary>A contact as returned by the list/detail/create/update endpoints.</summary>
public record ContactResponse(
    Guid Id,
    string Phone,
    string Name,
    bool IsActive,
    DateTime CreatedAt
);
