using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Subscriptions.Jobs;

/// <summary>
/// Processes one PayHere notify callback asynchronously. Runs without an HTTP request (no tenant
/// context) so ALL db queries use <c>IgnoreQueryFilters</c> and <c>CompanyId</c> is stamped
/// EXPLICITLY on new rows — same pattern as <c>StripeWebhookProcessingJob</c>. Unlike Stripe,
/// PayHere carries no long-lived customer object: the <see cref="PayHereOrder"/> created at
/// checkout time (keyed by its own Id == PayHere's order_id) is both the correlation row and the
/// idempotency ledger.
/// </summary>
[AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public class PayHereWebhookProcessingJob(
    AppDbContext db,
    ILogger<PayHereWebhookProcessingJob> logger)
{
    private const int PeriodDays = 30;

    public async Task ProcessAsync(string fieldsJson, CancellationToken ct = default)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(fieldsJson) ?? [];
        fields.TryGetValue("order_id", out var orderIdRaw);
        fields.TryGetValue("status_code", out var statusCode);
        fields.TryGetValue("status_message", out var statusMessage);
        fields.TryGetValue("payment_id", out var paymentId);

        if (!Guid.TryParse(orderIdRaw, out var orderId))
        {
            logger.LogError("PayHere webhook: order_id {OrderId} is not a valid GUID — dropped.", orderIdRaw);
            return;
        }

        // The DbContext runs an EnableRetryOnFailure execution strategy, which forbids a bare
        // user-initiated BeginTransaction (it must own the whole retriable unit), so the
        // transaction is opened INSIDE strategy.ExecuteAsync. Clearing the change tracker at the
        // top of each attempt keeps a retry from re-applying the prior attempt's mutations.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var order = await db.PayHereOrders.IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order is null)
            {
                logger.LogError("PayHere webhook: no PayHereOrder for order_id {OrderId} — dropped.", orderId);
                return;
            }

            switch (statusCode)
            {
                case "2": // success
                    if (order.Status != PayHereOrderStatus.Pending)
                    {
                        logger.LogInformation(
                            "PayHere order {OrderId} already {Status} — skipping success replay.",
                            order.Id, order.Status);
                        break;
                    }
                    await ApplySuccessAsync(order, paymentId, ct);
                    break;

                case "-1": // customer cancelled/dismissed
                    if (order.Status == PayHereOrderStatus.Pending)
                    {
                        order.Status = PayHereOrderStatus.Cancelled;
                        order.StatusMessage = statusMessage;
                    }
                    break;

                case "-2": // payment failed
                    if (order.Status == PayHereOrderStatus.Pending)
                    {
                        order.Status = PayHereOrderStatus.Failed;
                        order.StatusMessage = statusMessage;
                    }
                    break;

                case "-3": // charged back — can arrive AFTER the order already reached Completed
                    if (order.Status != PayHereOrderStatus.ChargedBack)
                    {
                        order.Status = PayHereOrderStatus.ChargedBack;
                        order.StatusMessage = statusMessage;
                        await DeactivateSubscriptionAsync(order, ct);
                    }
                    break;

                default:
                    logger.LogDebug(
                        "PayHere status_code {StatusCode} ignored for order {OrderId} (pending/unrecognized).",
                        statusCode, order.Id);
                    break;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>
    /// Applies the same "stack or fresh" assignment the manual SuperAdmin path uses, then records
    /// the purchase and marks the order Completed. Called only once per order (guarded by the
    /// Pending check in <see cref="ProcessAsync"/>).
    /// </summary>
    private async Task ApplySuccessAsync(PayHereOrder order, string? paymentId, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == order.PlanId, ct); // Plan has no tenant filter
        if (plan is null)
        {
            logger.LogError(
                "PayHere order {OrderId}: plan {PlanId} not found — cannot apply, order left Pending.",
                order.Id, order.PlanId);
            return;
        }

        var now = DateTime.UtcNow;

        var existing = await db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Plan)
            .Where(s => s.CompanyId == order.CompanyId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        Subscription sub;
        SubscriptionPurchaseMode mode;

        if (existing is not null && SubscriptionAssignmentEngine.IsLive(existing, now))
        {
            SubscriptionAssignmentEngine.ApplyStack(existing, plan, PeriodDays);
            sub = existing;
            mode = SubscriptionPurchaseMode.Stack;
        }
        else
        {
            if (existing is not null)
            {
                // Cancel any other active subs for this company first — unique filtered index
                // guard, same as StripeWebhookProcessingJob.SyncSubscriptionAsync.
                var actives = await db.Subscriptions.IgnoreQueryFilters()
                    .Where(s => s.CompanyId == order.CompanyId && s.Status == SubscriptionStatus.Active)
                    .ToListAsync(ct);
                foreach (var a in actives) { a.Status = SubscriptionStatus.Cancelled; a.IsActive = false; }
            }

            sub = SubscriptionAssignmentEngine.BuildFresh(order.CompanyId, plan, now, PeriodDays);
            db.Subscriptions.Add(sub);
            mode = SubscriptionPurchaseMode.Fresh;
        }

        var purchase = SubscriptionAssignmentEngine.BuildPurchase(order.CompanyId, sub, plan, mode, PeriodDays);
        purchase.PayHereOrderId = order.Id;
        db.SubscriptionPurchases.Add(purchase);

        order.Status = PayHereOrderStatus.Completed;
        order.PayHerePaymentId = paymentId;
        order.CompletedAt = now;
        order.SubscriptionId = sub.Id;

        logger.LogInformation(
            "PayHere order {OrderId} completed → company {CompanyId}, plan {PlanId}, mode {Mode}",
            order.Id, order.CompanyId, plan.Id, mode);
    }

    private async Task DeactivateSubscriptionAsync(PayHereOrder order, CancellationToken ct)
    {
        if (order.SubscriptionId is not { } subId) return;

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == subId, ct);
        if (sub is null) return;

        sub.Status = SubscriptionStatus.Inactive;
        sub.IsActive = false;
        logger.LogWarning("PayHere order {OrderId} charged back — subscription {SubId} marked Inactive.",
            order.Id, subId);
    }
}
