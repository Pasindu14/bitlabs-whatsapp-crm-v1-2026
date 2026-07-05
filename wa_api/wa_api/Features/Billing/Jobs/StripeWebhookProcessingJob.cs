using Hangfire;
using Microsoft.EntityFrameworkCore;
using Stripe;
using wa_api.Infrastructure.Persistence;
using LocalInvoice = wa_api.Features.Billing.Entities.Invoice;
using LocalSub = wa_api.Features.Subscriptions.Entities.Subscription;
using SubStatus = wa_api.Features.Subscriptions.Entities.SubscriptionStatus;

namespace wa_api.Features.Billing.Jobs;

/// <summary>
/// Processes one Stripe webhook event asynchronously. Runs without an HTTP request (no tenant
/// context) so ALL db queries use <c>IgnoreQueryFilters</c> and <c>CompanyId</c> is stamped
/// EXPLICITLY on new rows — same pattern as <c>InboundMessageWebhookHandler</c>.
/// Idempotent per-event: subscription updates are upserted by StripeSubscriptionId; invoices
/// have a unique index on StripeInvoiceId.
/// </summary>
[AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public class StripeWebhookProcessingJob(
    AppDbContext db,
    ILogger<StripeWebhookProcessingJob> logger)
{
    // Event type string constants — same values as Stripe.Events but we spell them out
    // to avoid a namespace-resolution race with the local Stripe alias.
    private const string SubCreated = "customer.subscription.created";
    private const string SubUpdated = "customer.subscription.updated";
    private const string SubDeleted = "customer.subscription.deleted";
    private const string InvPaid = "invoice.paid";
    private const string InvFailed = "invoice.payment_failed";

    public async Task ProcessAsync(string eventType, string eventJson, CancellationToken ct = default)
    {
        var stripeEvent = EventUtility.ParseEvent(eventJson);

        // Idempotency: Stripe delivers at-least-once and this job carries a retry budget, so the same event
        // can arrive twice. Without this guard a redelivered invoice.paid re-ran its handler and re-zeroed
        // MessagesUsedThisPeriod — silently wiping a whole period's usage (free quota). Process + record the
        // dedup marker in ONE transaction: a replay finds the marker and skips; a mid-processing failure
        // rolls both the effects AND the marker back so a genuine retry re-applies cleanly.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == stripeEvent.Id, ct))
        {
            logger.LogInformation("Stripe event {EventId} ({Type}) already processed — skipping replay.",
                stripeEvent.Id, eventType);
            return;
        }

        switch (eventType)
        {
            case SubCreated:
            case SubUpdated:
                if (stripeEvent.Data.Object is global::Stripe.Subscription sub)
                    await SyncSubscriptionAsync(sub, ct);
                break;

            case SubDeleted:
                if (stripeEvent.Data.Object is global::Stripe.Subscription subDel)
                    await CancelSubscriptionAsync(subDel, ct);
                break;

            case InvPaid:
                if (stripeEvent.Data.Object is global::Stripe.Invoice inv)
                    await UpsertInvoiceAsync(inv, ct);
                break;

            case InvFailed:
                if (stripeEvent.Data.Object is global::Stripe.Invoice invFailed)
                    await MarkSubscriptionInactiveAsync(invFailed, ct);
                break;

            default:
                logger.LogDebug("Stripe event {EventType} ignored (no handler)", eventType);
                break;
        }

        db.ProcessedStripeEvents.Add(new Entities.ProcessedStripeEvent
        {
            StripeEventId = stripeEvent.Id,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ── Subscription sync ────────────────────────────────────────────────────

    private async Task SyncSubscriptionAsync(global::Stripe.Subscription stripeSub, CancellationToken ct)
    {
        var company = await FindCompanyByCustomerAsync(stripeSub.CustomerId, ct);
        if (company is null) return;

        var priceId = stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (priceId is null)
        {
            logger.LogWarning("Stripe subscription {SubId} has no price item — skipped", stripeSub.Id);
            return;
        }

        var plan = await db.Plans.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.StripePriceId == priceId, ct);

        if (plan is null)
        {
            logger.LogError(
                "Stripe subscription {SubId}: no plan with StripePriceId={PriceId}. " +
                "Check plan configuration — subscription NOT synced.", stripeSub.Id, priceId);
            return;
        }

        var existing = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSub.Id, ct)
            ?? await db.Subscriptions.IgnoreQueryFilters()
                .Where(s => s.CompanyId == company.Id && s.Status == SubStatus.Active)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

        var newStatus = MapStripeStatus(stripeSub.Status);
        // In Stripe.net v52, CurrentPeriodStart/End moved from Subscription to SubscriptionItem.
        var firstItem = stripeSub.Items?.Data?.FirstOrDefault();
        var periodStart = firstItem?.CurrentPeriodStart ?? DateTime.UtcNow;
        var periodEnd = firstItem?.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);

        if (existing is not null)
        {
            var resetUsage = existing.PlanId != plan.Id || existing.CurrentPeriodEnd < periodStart;
            existing.PlanId = plan.Id;
            existing.StripeSubscriptionId = stripeSub.Id;
            existing.Status = newStatus;
            existing.CurrentPeriodStart = periodStart;
            existing.CurrentPeriodEnd = periodEnd;
            if (resetUsage) existing.MessagesUsedThisPeriod = 0;
            existing.IsActive = newStatus == SubStatus.Active;
        }
        else
        {
            // Cancel any manual active subscription before inserting (unique filtered index guard).
            var manuals = await db.Subscriptions.IgnoreQueryFilters()
                .Where(s => s.CompanyId == company.Id && s.Status == SubStatus.Active)
                .ToListAsync(ct);
            foreach (var m in manuals) { m.Status = SubStatus.Cancelled; m.IsActive = false; }
            if (manuals.Count > 0) await db.SaveChangesAsync(ct);

            db.Subscriptions.Add(new LocalSub
            {
                CompanyId = company.Id,
                PlanId = plan.Id,
                StripeSubscriptionId = stripeSub.Id,
                Status = newStatus,
                CurrentPeriodStart = periodStart,
                CurrentPeriodEnd = periodEnd,
                MessagesUsedThisPeriod = 0,
                IsActive = newStatus == SubStatus.Active,
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Synced Stripe subscription {SubId} → company {CompanyId}, plan {PlanId}, status {Status}",
            stripeSub.Id, company.Id, plan.Id, newStatus);
    }

    private async Task CancelSubscriptionAsync(global::Stripe.Subscription stripeSub, CancellationToken ct)
    {
        var sub = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSub.Id, ct);

        if (sub is null)
        {
            logger.LogWarning("subscription.deleted for unknown Stripe sub {SubId} — ignored", stripeSub.Id);
            return;
        }

        sub.Status = SubStatus.Cancelled;
        sub.IsActive = false;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cancelled subscription {SubId} for company {CompanyId}", stripeSub.Id, sub.CompanyId);
    }

    // ── Invoice ──────────────────────────────────────────────────────────────

    private async Task UpsertInvoiceAsync(global::Stripe.Invoice inv, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inv.CustomerId)) return;

        var company = await FindCompanyByCustomerAsync(inv.CustomerId, ct);
        if (company is null) return;

        // In Stripe.net v52, subscription ID is nested under Invoice.Parent.SubscriptionDetails.
        var stripeSubId = inv.Parent?.SubscriptionDetails?.SubscriptionId;
        // StatusTransitions removed in v52; EffectiveAt is when the invoice was paid/finalized.
        var paidAt = inv.EffectiveAt;

        var existing = await db.Invoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == inv.Id, ct);

        if (existing is not null)
        {
            existing.Status = inv.Status ?? "paid";
            existing.PaidAt = paidAt;
            existing.HostedInvoiceUrl = inv.HostedInvoiceUrl;
            existing.InvoicePdfUrl = inv.InvoicePdf;
        }
        else
        {
            db.Invoices.Add(new LocalInvoice
            {
                CompanyId = company.Id,
                StripeInvoiceId = inv.Id,
                StripeSubscriptionId = stripeSubId,
                AmountPaid = inv.AmountPaid,
                Currency = inv.Currency ?? "usd",
                Status = inv.Status ?? "paid",
                PaidAt = paidAt,
                HostedInvoiceUrl = inv.HostedInvoiceUrl,
                InvoicePdfUrl = inv.InvoicePdf,
                IsActive = true,
            });
        }

        // Reset the message-quota counter ONLY when this invoice genuinely opens a NEW billing period.
        // invoice.paid also fires for redeliveries, late payments of OLD invoices, and mid-period proration
        // invoices; resetting unconditionally on any of those would wipe an in-progress period and hand the
        // tenant a free full-quota refill. Guard on the invoice's billing period vs the recorded period start,
        // and advance the stored period when we reset — so this path and SyncSubscriptionAsync converge:
        // whichever renewal event (invoice.paid or customer.subscription.updated) arrives first performs the
        // single reset, and the other then sees the period already advanced and does nothing.
        if (!string.IsNullOrWhiteSpace(stripeSubId))
        {
            var sub = await db.Subscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);

            if (sub is not null && InvoiceBillingPeriod(inv) is { } period
                && ShouldResetUsage(inv.BillingReason, sub.CurrentPeriodStart, period.Start))
            {
                sub.MessagesUsedThisPeriod = 0;
                sub.CurrentPeriodStart = period.Start;
                if (period.End > sub.CurrentPeriodEnd) sub.CurrentPeriodEnd = period.End;
                logger.LogInformation(
                    "invoice.paid opened new period {Start:o} for subscription {SubId} — usage reset.",
                    period.Start, stripeSubId);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Upserted invoice {InvoiceId} for company {CompanyId}", inv.Id, company.Id);
    }

    /// <summary>
    /// Whether an <c>invoice.paid</c> should reset the usage counter. True only for a genuine renewal cycle:
    /// Stripe's <c>BillingReason</c> is <c>subscription_cycle</c> (or <c>subscription_create</c>) AND the
    /// invoice opens a period STRICTLY newer than the one already recorded. This is a two-part guard because
    /// neither signal alone is sufficient:
    /// <list type="bullet">
    /// <item>Period-start alone would fire on a mid-cycle <b>proration</b> invoice (BillingReason
    /// <c>subscription_update</c>), whose line period also starts after the recorded period start — wrongly
    /// wiping the active period. The BillingReason gate excludes those.</item>
    /// <item>BillingReason alone would fire on a <b>redelivered</b> or <b>late</b> cycle invoice for an old
    /// period. The strict period-advance gate excludes those (their start is not newer than what we recorded).</item>
    /// </list>
    /// </summary>
    public static bool ShouldResetUsage(
        string? billingReason, DateTime recordedPeriodStart, DateTime? invoicePeriodStart)
    {
        if (billingReason is not ("subscription_cycle" or "subscription_create"))
            return false;

        return invoicePeriodStart is DateTime start && start > recordedPeriodStart;
    }

    /// <summary>
    /// The newest subscription billing period covered by the invoice's line items, or null when it carries
    /// none. On a renewal this is the new period; a mixed invoice (proration + renewal) yields the latest
    /// line period so the renewal drives the decision; a pure proration/one-off invoice yields a period that
    /// falls within the current one and therefore won't advance it.
    /// </summary>
    public static (DateTime Start, DateTime End)? InvoiceBillingPeriod(global::Stripe.Invoice inv)
    {
        var periods = inv.Lines?.Data?
            .Where(l => l.Period is not null)
            .Select(l => (Start: l.Period.Start, End: l.Period.End))
            .ToList();

        return periods is { Count: > 0 }
            ? periods.OrderByDescending(p => p.Start).First()
            : null;
    }

    private async Task MarkSubscriptionInactiveAsync(global::Stripe.Invoice inv, CancellationToken ct)
    {
        // In Stripe.net v52, subscription ID is under Invoice.Parent.SubscriptionDetails.
        var stripeSubId = inv.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrWhiteSpace(stripeSubId)) return;

        var sub = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);
        if (sub is null) return;

        sub.Status = SubStatus.Inactive;
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Marked subscription {SubId} Inactive due to payment failure", stripeSubId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Features.Companies.Company?> FindCompanyByCustomerAsync(
        string stripeCustomerId, CancellationToken ct)
    {
        var company = await db.Companies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);
        if (company is null)
            logger.LogError("No company found for Stripe customer {CustomerId} — event skipped", stripeCustomerId);
        return company;
    }

    private static SubStatus MapStripeStatus(string? status) => status switch
    {
        "active" or "trialing" => SubStatus.Active,
        "canceled" => SubStatus.Cancelled,
        _ => SubStatus.Inactive,
    };
}
