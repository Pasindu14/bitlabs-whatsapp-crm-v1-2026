using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>
/// Billing details collected in the frontend's checkout dialog — PayHere rejects empty
/// address/city/country, and the app doesn't otherwise persist billing-profile fields on
/// Company/User, so these are collected fresh on every checkout rather than stored.
/// </summary>
public record PayHereCheckoutRequest(
    [Required] Guid PlanId,
    [Required] string FirstName,
    [Required] string LastName,
    [Required] string Phone,
    [Required] string Address,
    [Required] string City,
    [Required] string Country);

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
