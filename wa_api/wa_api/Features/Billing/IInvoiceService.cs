using wa_api.Features.Billing.Dtos;

namespace wa_api.Features.Billing;

public interface IInvoiceService
{
    /// <summary>Lists all invoices for the calling company, newest first.</summary>
    Task<IReadOnlyList<InvoiceResponse>> GetForCallerAsync(CancellationToken ct = default);
}
