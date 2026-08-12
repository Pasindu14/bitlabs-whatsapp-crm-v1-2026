using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Billing;
using wa_api.Features.Billing.Dtos;
using wa_api.Features.Subscriptions.Dtos;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.PayHere;
using wa_api.Infrastructure.Persistence;
using wa_api.Infrastructure.Stripe;

namespace wa_api.Features.Subscriptions.Controllers;

/// <summary>
/// Tenant-facing subscription endpoints. <c>CompanyId</c> always comes from the JWT —
/// never from the client. Billing actions (checkout, portal) are restricted to
/// <c>CompanyAdmin</c> role; read endpoints are open to any authenticated company user.
/// </summary>
[ApiController]
[Route("my-subscription")]
[Authorize]
public class MySubscriptionController(
    ISubscriptionService service,
    IInvoiceService invoiceService,
    IStripeService stripeService,
    IPayHereService payHereService,
    AppDbContext db,
    ITenantContext tenant) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>GET /api/v1/my-subscription — active plan + usage (empty shape if none).</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var result = await service.GetMineAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/my-subscription/invoices — invoice history for the calling company.</summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(CancellationToken ct)
    {
        var result = await invoiceService.GetForCallerAsync(ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>
    /// GET /api/v1/my-subscription/subscription-history — every SuperAdmin plan
    /// assignment/change for the calling company, newest first (the manual counterpart to
    /// Stripe invoices, since assignment in this phase is SuperAdmin-only, not self-checkout).
    /// </summary>
    [HttpGet("subscription-history")]
    public async Task<IActionResult> GetSubscriptionHistory(CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("subscription-history");

        var result = await service.GetSubscriptionHistoryAsync(companyId, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>
    /// GET /api/v1/my-subscription/available-plans — active plans purchasable via self-service
    /// checkout, either through Stripe (<c>StripePriceId</c> set) or PayHere (<c>IsOnline</c>).
    /// Read by any authenticated company user to populate the "Upgrade" plan picker.
    /// </summary>
    [HttpGet("available-plans")]
    public async Task<IActionResult> GetAvailablePlans(CancellationToken ct)
    {
        var plans = await db.Plans
            .AsNoTracking()
            .Where(p => p.IsActive && (p.StripePriceId != null || p.IsOnline))
            .OrderBy(p => p.Price)
            .Select(p => new AvailablePlanResponse(
                p.Id, p.Name, p.MonthlyMessageQuota, p.Price, p.Currency, p.StripePriceId, p.IsOnline))
            .ToListAsync(ct);

        return Ok(ResponseHelper.Ok(plans, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/my-subscription/checkout — creates a Stripe Checkout session and returns
    /// the redirect URL. <b>CompanyAdmin only.</b>
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("checkout");

        var plan = await db.Plans.FindAsync([request.PlanId], ct)
            ?? throw new NotFoundException("Plan", request.PlanId);

        if (!plan.IsActive)
            throw new BusinessRuleException("PLAN_INACTIVE", "The selected plan is inactive.");

        if (string.IsNullOrWhiteSpace(plan.StripePriceId))
            throw new BusinessRuleException(
                "PLAN_NOT_SELF_SERVICE",
                "This plan cannot be purchased via self-service checkout. Contact your platform administrator.");

        var company = await db.Companies.FindAsync([companyId], ct)
            ?? throw new NotFoundException("Company", companyId);

        var customerId = await stripeService.EnsureCustomerAsync(
            companyId, company.Name, company.Email, ct);

        var url = await stripeService.CreateCheckoutSessionAsync(
            customerId, plan.StripePriceId, companyId, ct);

        return Ok(ResponseHelper.Ok(new CheckoutSessionResponse(url), CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/my-subscription/portal — creates a Stripe Billing Portal session and returns
    /// the redirect URL. <b>CompanyAdmin only.</b> Requires the company to already have a Stripe
    /// subscription (i.e. they completed checkout at least once).
    /// </summary>
    [HttpPost("portal")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> CreatePortal(CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("portal");

        var company = await db.Companies.FindAsync([companyId], ct)
            ?? throw new NotFoundException("Company", companyId);

        if (string.IsNullOrWhiteSpace(company.StripeCustomerId))
            throw new BusinessRuleException(
                "NO_STRIPE_SUBSCRIPTION",
                "No Stripe billing account found. Complete a checkout first or contact your administrator.");

        var url = await stripeService.CreatePortalSessionAsync(company.StripeCustomerId, ct);
        return Ok(ResponseHelper.Ok(new PortalSessionResponse(url), CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/my-subscription/payhere-checkout — creates a Pending <see cref="PayHereOrder"/>
    /// and returns the signed payload for <c>payhere.startPayment()</c>. <b>CompanyAdmin only.</b>
    /// Unlike Stripe this is a one-time payment (no recurring auto-billing): the outcome is applied
    /// by <c>PayHereWebhookProcessingJob</c> once PayHere's notify callback confirms payment.
    /// </summary>
    [HttpPost("payhere-checkout")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> CreatePayHereCheckout([FromBody] PayHereCheckoutRequest request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("payhere-checkout");

        var plan = await db.Plans.FindAsync([request.PlanId], ct)
            ?? throw new NotFoundException("Plan", request.PlanId);

        if (!plan.IsActive)
            throw new BusinessRuleException("PLAN_INACTIVE", "The selected plan is inactive.");

        if (!plan.IsOnline)
            throw new BusinessRuleException(
                "PLAN_NOT_SELF_SERVICE",
                "This plan cannot be purchased via self-service checkout. Contact your platform administrator.");

        var company = await db.Companies.FindAsync([companyId], ct)
            ?? throw new NotFoundException("Company", companyId);

        var order = new PayHereOrder
        {
            CompanyId = companyId,
            PlanId = plan.Id,
            Amount = plan.Price,
            Currency = plan.Currency,
        };
        db.PayHereOrders.Add(order);
        await db.SaveChangesAsync(ct);

        var payload = payHereService.BuildCheckoutPayload(order, plan, company);
        return Ok(ResponseHelper.Ok(payload, CorrelationId));
    }
}
