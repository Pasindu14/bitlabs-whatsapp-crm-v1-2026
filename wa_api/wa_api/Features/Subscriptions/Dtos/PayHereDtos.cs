using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Subscriptions.Dtos;

public record PayHereCheckoutRequest([Required] Guid PlanId);

/// <summary>
/// The fields PayHere's onsite JS (<c>payhere.startPayment()</c>) needs, including the
/// server-computed <see cref="Hash"/>. Returned as-is to the frontend — never a redirect URL,
/// since PayHere checkout is a signed client-side payload, not a hosted-session API call.
/// </summary>
public record PayHereCheckoutResponse(
    string MerchantId,
    string OrderId,
    string Items,
    string Amount,
    string Currency,
    string Hash,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Address,
    string City,
    string Country,
    string NotifyUrl,
    string ReturnUrl,
    string CancelUrl,
    bool Sandbox);
