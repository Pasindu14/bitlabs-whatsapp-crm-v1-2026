using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Billing.Entities;

/// <summary>
/// A paid Stripe invoice mirrored locally. Written by the webhook job (no HTTP tenant context),
/// so <see cref="CompanyId"/> is stamped EXPLICITLY — not via the <c>AuditInterceptor</c>.
/// Idempotent: the unique index on <see cref="StripeInvoiceId"/> ensures replayed
/// <c>invoice.paid</c> webhooks do a no-op on conflict.
/// </summary>
public class Invoice : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Stripe Invoice ID (<c>in_xxx</c>). Unique platform-wide.</summary>
    public string StripeInvoiceId { get; set; } = null!;

    /// <summary>Stripe Subscription ID that generated this invoice.</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Amount paid in the smallest currency unit (e.g. cents for USD).</summary>
    public long AmountPaid { get; set; }

    /// <summary>ISO 4217 currency code, lowercase (e.g. "usd").</summary>
    public string Currency { get; set; } = "usd";

    /// <summary>Stripe invoice status at time of sync.</summary>
    public string Status { get; set; } = "paid";

    public DateTime? PaidAt { get; set; }

    /// <summary>Stripe-hosted HTML invoice URL (shown to company users).</summary>
    public string? HostedInvoiceUrl { get; set; }

    /// <summary>PDF download URL for the invoice.</summary>
    public string? InvoicePdfUrl { get; set; }
}
