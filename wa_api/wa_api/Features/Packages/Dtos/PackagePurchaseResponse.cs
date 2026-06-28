using wa_api.Features.Packages.Entities;

namespace wa_api.Features.Packages.Dtos;

/// <summary>A single "package added to a company" audit row.</summary>
public record PackagePurchaseResponse(
    Guid Id,
    Guid CompanyId,
    Guid SubscriptionId,
    Guid PackageId,
    string PackageName,
    int MessagesAdded,
    decimal Price,
    string Currency,
    DateTime CreatedAt
)
{
    public static PackagePurchaseResponse From(PackagePurchase p)
        => new(p.Id, p.CompanyId, p.SubscriptionId, p.PackageId, p.PackageName,
               p.MessagesAdded, p.Price, p.Currency, p.CreatedAt);
}
