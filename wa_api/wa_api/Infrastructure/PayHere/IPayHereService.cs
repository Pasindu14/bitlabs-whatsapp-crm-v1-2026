using Microsoft.AspNetCore.Http;
using wa_api.Features.Companies;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Dtos;
using wa_api.Features.Subscriptions.Entities;

namespace wa_api.Infrastructure.PayHere;

public interface IPayHereService
{
    /// <summary>Builds the signed checkout payload for <c>payhere.startPayment()</c>.</summary>
    PayHereCheckoutResponse BuildCheckoutPayload(PayHereOrder order, Plan plan, Company company);

    /// <summary>Recomputes PayHere's notify-callback md5sig and compares it against the posted one.</summary>
    bool VerifyNotifySignature(IFormCollection form);
}
