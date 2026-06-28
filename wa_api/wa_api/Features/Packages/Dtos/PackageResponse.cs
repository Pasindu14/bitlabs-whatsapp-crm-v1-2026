using wa_api.Features.Packages.Entities;

namespace wa_api.Features.Packages.Dtos;

/// <summary>A message-credit package as returned by the catalog endpoints.</summary>
public record PackageResponse(
    Guid Id,
    string Name,
    string? Description,
    int ExtraMessages,
    decimal Price,
    string Currency,
    bool IsActive,
    DateTime CreatedAt
)
{
    public static PackageResponse From(MessagePackage p)
        => new(p.Id, p.Name, p.Description, p.ExtraMessages, p.Price, p.Currency, p.IsActive, p.CreatedAt);
}
