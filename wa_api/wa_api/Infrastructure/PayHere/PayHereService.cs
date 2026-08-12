using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using wa_api.Features.Companies;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Dtos;
using wa_api.Features.Subscriptions.Entities;

namespace wa_api.Infrastructure.PayHere;

/// <summary>
/// Builds/verifies PayHere's MD5-based checkout hash and notify-callback signature. PayHere has
/// no SDK (unlike Stripe) — its "API" is a documented hash formula over a handful of fields, so
/// this is the whole gateway integration, no HTTP calls out.
/// </summary>
public sealed class PayHereService(IOptions<PayHereOptions> options) : IPayHereService
{
    private readonly PayHereOptions _opts = options.Value;

    public PayHereCheckoutResponse BuildCheckoutPayload(PayHereOrder order, Plan plan, Company company)
    {
        var orderId = order.Id.ToString();
        var amount = FormatAmount(order.Amount);
        var hash = ComputeCheckoutHash(orderId, amount, order.Currency);
        var (firstName, lastName) = SplitCompanyName(company.Name);

        return new PayHereCheckoutResponse(
            MerchantId: _opts.MerchantId,
            OrderId: orderId,
            Items: plan.Name,
            Amount: amount,
            Currency: order.Currency,
            Hash: hash,
            FirstName: firstName,
            LastName: lastName,
            Email: company.Email ?? "",
            // PayHere rejects empty phone/address/city/country, but the app has no billing-profile
            // fields on Company beyond Phone — these are receipt metadata only, never re-collected
            // or validated by PayHere's own onsite widget (it only asks for card details), so a
            // fixed placeholder is fine where Company doesn't have real data.
            Phone: string.IsNullOrWhiteSpace(company.Phone) ? "0000000000" : company.Phone,
            Address: "N/A",
            City: "N/A",
            Country: "United Arab Emirates",
            NotifyUrl: _opts.NotifyUrl,
            ReturnUrl: _opts.ReturnUrl,
            CancelUrl: _opts.CancelUrl,
            Sandbox: _opts.Sandbox);
    }

    /// <summary>Company has one Name field; PayHere wants first/last separately.</summary>
    private static (string FirstName, string LastName) SplitCompanyName(string companyName)
    {
        var parts = companyName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("Customer", "Account"),
            1 => (parts[0], "Account"),
            _ => (parts[0], parts[1]),
        };
    }

    public bool VerifyNotifySignature(IFormCollection form)
    {
        var merchantId = form["merchant_id"].ToString();
        // Merchant ID mismatch means this notify isn't even for our account — reject before hashing.
        if (merchantId != _opts.MerchantId)
            return false;

        var receivedSig = form["md5sig"].ToString();
        if (string.IsNullOrEmpty(receivedSig))
            return false;

        var expected = ComputeNotifyHash(
            merchantId,
            form["order_id"].ToString(),
            form["payhere_amount"].ToString(),
            form["payhere_currency"].ToString(),
            form["status_code"].ToString());

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var receivedBytes = Encoding.UTF8.GetBytes(receivedSig.ToUpperInvariant());
        return expectedBytes.Length == receivedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
    }

    // hash = strtoupper(md5(merchant_id + order_id + amount + currency + strtoupper(md5(merchant_secret))))
    private string ComputeCheckoutHash(string orderId, string amount, string currency)
        => Md5Hex(_opts.MerchantId + orderId + amount + currency + HashedSecret);

    // local_md5sig = strtoupper(md5(merchant_id + order_id + payhere_amount + payhere_currency + status_code + strtoupper(md5(merchant_secret))))
    private string ComputeNotifyHash(string merchantId, string orderId, string amount, string currency, string statusCode)
        => Md5Hex(merchantId + orderId + amount + currency + statusCode + HashedSecret);

    private string HashedSecret => Md5Hex(_opts.MerchantSecret);

    private static string Md5Hex(string input) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input)));

    /// <summary>PayHere requires exactly 2 decimal places, invariant culture, no thousands separator.</summary>
    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
}
