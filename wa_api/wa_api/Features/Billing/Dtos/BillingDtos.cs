using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Billing.Dtos;

public record CheckoutRequest([Required] Guid PlanId);

public record CheckoutSessionResponse(string Url);

public record PortalSessionResponse(string Url);

public record InvoiceResponse(
    Guid Id,
    string StripeInvoiceId,
    decimal AmountPaid,
    string Currency,
    string Status,
    DateTime? PaidAt,
    string? HostedInvoiceUrl,
    string? InvoicePdfUrl,
    DateTime CreatedAt);

public record AvailablePlanResponse(
    Guid Id,
    string Name,
    int MonthlyMessageQuota,
    decimal Price,
    string Currency,
    string? StripePriceId);
