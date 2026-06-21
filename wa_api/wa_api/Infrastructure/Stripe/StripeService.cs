using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Use global:: prefix throughout to avoid resolution against our own wa_api.Infrastructure.Stripe namespace.
using StripeLib = global::Stripe;

namespace wa_api.Infrastructure.Stripe;

/// <summary>
/// Thin wrapper around the Stripe SDK. Scoped so it shares the <c>AppDbContext</c> that it
/// needs to persist <c>StripeCustomerId</c> back onto the <see cref="Features.Companies.Company"/> row.
/// Uses <c>global::Stripe</c> aliases everywhere to prevent the compiler from resolving "Stripe"
/// against our own namespace (<c>wa_api.Infrastructure.Stripe</c>).
/// </summary>
public sealed class StripeService(
    IOptions<StripeOptions> options,
    Infrastructure.Persistence.AppDbContext db,
    ILogger<StripeService> logger) : IStripeService
{
    private readonly StripeOptions _opts = options.Value;

    public async Task<string> EnsureCustomerAsync(
        Guid companyId, string companyName, string? email, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new InvalidOperationException($"Company {companyId} not found.");

        if (!string.IsNullOrWhiteSpace(company.StripeCustomerId))
            return company.StripeCustomerId;

        StripeLib.StripeConfiguration.ApiKey = _opts.SecretKey;
        var customer = await new StripeLib.CustomerService().CreateAsync(
            new StripeLib.CustomerCreateOptions
            {
                Name = companyName,
                Email = email,
                Metadata = new Dictionary<string, string> { { "companyId", companyId.ToString() } }
            }, cancellationToken: ct);

        company.StripeCustomerId = customer.Id;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created Stripe customer {CustomerId} for company {CompanyId}",
            customer.Id, companyId);
        return customer.Id;
    }

    public async Task<string> CreateCheckoutSessionAsync(
        string stripeCustomerId, string stripePriceId, Guid companyId, CancellationToken ct = default)
    {
        StripeLib.StripeConfiguration.ApiKey = _opts.SecretKey;
        var session = await new global::Stripe.Checkout.SessionService().CreateAsync(
            new global::Stripe.Checkout.SessionCreateOptions
            {
                Customer = stripeCustomerId,
                Mode = "subscription",
                LineItems = [new global::Stripe.Checkout.SessionLineItemOptions { Price = stripePriceId, Quantity = 1 }],
                SuccessUrl = _opts.SuccessUrl,
                CancelUrl = _opts.CancelUrl,
                Metadata = new Dictionary<string, string> { { "companyId", companyId.ToString() } },
                SubscriptionData = new global::Stripe.Checkout.SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { { "companyId", companyId.ToString() } }
                }
            }, cancellationToken: ct);

        return session.Url;
    }

    public async Task<string> CreatePortalSessionAsync(
        string stripeCustomerId, CancellationToken ct = default)
    {
        StripeLib.StripeConfiguration.ApiKey = _opts.SecretKey;
        var session = await new global::Stripe.BillingPortal.SessionService().CreateAsync(
            new global::Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = stripeCustomerId,
                ReturnUrl = _opts.PortalReturnUrl
            }, cancellationToken: ct);

        return session.Url;
    }
}
