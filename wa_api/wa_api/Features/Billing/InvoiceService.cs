using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Billing.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Billing;

public class InvoiceService(AppDbContext db, ITenantContext tenant) : IInvoiceService
{
    public async Task<IReadOnlyList<InvoiceResponse>> GetForCallerAsync(CancellationToken ct = default)
    {
        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("invoices");

        // Global query filter already scopes to the caller's company.
        var invoices = await db.Invoices
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.PaidAt ?? i.CreatedAt)
            .ToListAsync(ct);

        return invoices.Select(i => new InvoiceResponse(
            i.Id,
            i.StripeInvoiceId,
            StripeCurrency.ToMajorUnit(i.AmountPaid, i.Currency),   // smallest unit → major unit (zero-decimal aware)
            i.Currency.ToUpperInvariant(),
            i.Status,
            i.PaidAt,
            i.HostedInvoiceUrl,
            i.InvoicePdfUrl,
            i.CreatedAt)).ToList();
    }
}
