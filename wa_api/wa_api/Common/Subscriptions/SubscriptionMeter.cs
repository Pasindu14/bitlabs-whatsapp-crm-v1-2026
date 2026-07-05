using Microsoft.EntityFrameworkCore;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Common.Subscriptions;

/// <summary>
/// The single quota-enforcement point. BOTH send paths flow through this — the direct/inbox sender
/// (<c>WhatsAppMessageSender</c> / <c>MessageService</c>) and the bulk campaign sender
/// (<c>CampaignBatchSendJob</c>) — so a send can never move the counter without going through the same
/// atomic ceiling. Use <see cref="TryReserveAsync"/> BEFORE sending (reserve-then-send) and
/// <see cref="RefundAsync"/> if the send then fails.
/// </summary>
public interface ISubscriptionMeter
{
    /// <summary>
    /// Atomically reserve <paramref name="count"/> against the company's active subscription, but ONLY if it
    /// stays within the effective quota (plan quota + extra credits). Returns the id of the subscription that
    /// was charged, or null when there is no active subscription OR the reservation would exceed the ceiling.
    /// The conditional UPDATE is a single statement, so concurrent reservations are serialized by the row lock
    /// and the ceiling can never be overshot (no read-check-then-increment TOCTOU). Reserve BEFORE sending and
    /// <see cref="RefundAsync"/> the reservation if the send fails.
    /// </summary>
    Task<Guid?> TryReserveAsync(Guid companyId, int count = 1, CancellationToken ct = default);

    /// <summary>
    /// Give back <paramref name="count"/> to a subscription's counter (floored at 0), atomically. Used to undo
    /// a reservation when the send it was taken for fails, and by the delivery-failure webhook path.
    /// </summary>
    Task RefundAsync(Guid subscriptionId, int count = 1, CancellationToken ct = default);
}

public class SubscriptionMeter(AppDbContext db) : ISubscriptionMeter
{
    public async Task<Guid?> TryReserveAsync(Guid companyId, int count = 1, CancellationToken ct = default)
    {
        if (count <= 0 || companyId == Guid.Empty)
            return null;

        // Most-recent active subscription for the company. IgnoreQueryFilters: metering runs from the campaign
        // job (Hangfire) and webhook scopes, neither of which has the tenant query-filter context.
        var subId = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.CompanyId == companyId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (subId is null)
            return null;

        var now = DateTime.UtcNow;

        if (db.Database.IsRelational())
        {
            // Atomic conditional increment: the row is bumped ONLY when it stays within quota+credits. Two
            // concurrent reservations can't both slip past the cap — Postgres serializes the row update — so
            // there is no overshoot. 0 rows affected ⇒ ceiling reached.
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Subscriptions"" AS s
                SET ""MessagesUsedThisPeriod"" = s.""MessagesUsedThisPeriod"" + {count},
                    ""UpdatedAt"" = {now}
                FROM ""Plans"" AS p
                WHERE s.""Id"" = {subId.Value}
                  AND p.""Id"" = s.""PlanId""
                  AND s.""MessagesUsedThisPeriod"" + {count} <= p.""MonthlyMessageQuota"" + s.""ExtraMessageCredits""", ct);

            return affected == 1 ? subId : null;
        }

        // InMemory fallback (tests): tracked read-modify-write with the same ceiling check.
        var sub = await db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subId.Value, ct);
        if (sub?.Plan is null)
            return null;

        var ceiling = sub.Plan.MonthlyMessageQuota + sub.ExtraMessageCredits;
        if (sub.MessagesUsedThisPeriod + count > ceiling)
            return null;

        sub.MessagesUsedThisPeriod += count;
        sub.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return subId;
    }

    public async Task RefundAsync(Guid subscriptionId, int count = 1, CancellationToken ct = default)
    {
        if (count <= 0 || subscriptionId == Guid.Empty)
            return;

        var now = DateTime.UtcNow;

        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Subscriptions""
                SET ""MessagesUsedThisPeriod"" = GREATEST(0, ""MessagesUsedThisPeriod"" - {count}),
                    ""UpdatedAt"" = {now}
                WHERE ""Id"" = {subscriptionId}", ct);
            return;
        }

        var sub = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);
        if (sub is null)
            return;

        sub.MessagesUsedThisPeriod = Math.Max(0, sub.MessagesUsedThisPeriod - count);
        sub.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }
}
