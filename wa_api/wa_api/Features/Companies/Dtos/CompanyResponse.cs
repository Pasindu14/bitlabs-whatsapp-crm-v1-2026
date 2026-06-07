namespace wa_api.Features.Companies.Dtos;

/// <summary>A company as returned by the list/create endpoints.</summary>
public record CompanyResponse(
    Guid Id,
    string Name,
    string? Slug,
    string? Email,
    string? Phone,
    bool IsActive,
    DateTime CreatedAt
);
