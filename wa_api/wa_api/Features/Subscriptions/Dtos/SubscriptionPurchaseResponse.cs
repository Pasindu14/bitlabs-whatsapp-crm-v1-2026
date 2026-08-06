using wa_api.Features.Subscriptions.Entities;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>A single "company subscribed to a plan" audit row (newest first in the history list).</summary>
public record SubscriptionPurchaseResponse(
    Guid Id,
    Guid CompanyId,
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    SubscriptionPurchaseMode Mode,
    int MessagesAdded,
    int PeriodDays,
    int BalanceAfter,
    DateTime PeriodEndAfter,
    decimal Price,
    string Currency,
    DateTime CreatedAt,
    Guid? PayHereOrderId
)
{
    public static SubscriptionPurchaseResponse From(SubscriptionPurchase p)
        => new(p.Id, p.CompanyId, p.SubscriptionId, p.PlanId, p.PlanName, p.Mode,
               p.MessagesAdded, p.PeriodDays, p.BalanceAfter, p.PeriodEndAfter,
               p.Price, p.Currency, p.CreatedAt, p.PayHereOrderId);
}
