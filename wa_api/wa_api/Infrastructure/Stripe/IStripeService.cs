namespace wa_api.Infrastructure.Stripe;

public interface IStripeService
{
    /// <summary>
    /// Gets or creates a Stripe Customer for the company. Stores the returned ID on the company
    /// row so subsequent calls are a no-op. Call this before creating any checkout session.
    /// </summary>
    Task<string> EnsureCustomerAsync(Guid companyId, string companyName, string? email, CancellationToken ct = default);

    /// <summary>Creates a Stripe Checkout Session in subscription mode and returns the hosted URL.</summary>
    Task<string> CreateCheckoutSessionAsync(string stripeCustomerId, string stripePriceId, Guid companyId, CancellationToken ct = default);

    /// <summary>Creates a Stripe Billing Portal Session and returns the hosted URL.</summary>
    Task<string> CreatePortalSessionAsync(string stripeCustomerId, CancellationToken ct = default);
}
