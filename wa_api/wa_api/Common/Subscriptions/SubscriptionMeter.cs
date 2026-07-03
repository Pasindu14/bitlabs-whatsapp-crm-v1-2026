using Microsoft.EntityFrameworkCore;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Common.Subscriptions;

/// <summary>
/// Records message usage against a company's active subscription. BOTH send paths flow through this
/// one place — the direct/inbox sender (<c>WhatsAppMessageSender</c>) and the bulk campaign sender
/// (<c>CampaignBatchSendJob</c>) — so a send can never go out without moving the quota counter,
/// regardless of which path sent it.
/// </summary>
public interface ISubscriptionMeter
{
    /// <summary>
    /// Atomically add <paramref name="count"/> to the company's active-subscription
    /// <c>MessagesUsedThisPeriod</c>. No-op when <paramref name="count"/> ≤ 0 or the company has no
    /// active subscription. Safe from webhook/Hangfire scopes: the company is passed explicitly and
    /// the tenant query filter is ignored (those scopes have no <c>HttpContext</c>).
    /// </summary>
    Task ConsumeAsync(Guid companyId, int count = 1, CancellationToken ct = default);
}

public class SubscriptionMeter(AppDbContext db) : ISubscriptionMeter
{
    public async Task ConsumeAsync(Guid companyId, int count = 1, CancellationToken ct = default)
    {
        if (count <= 0 || companyId == Guid.Empty)
            return;

        // Most-recent active subscription for the company. IgnoreQueryFilters: metering runs from the
        // campaign job (Hangfire) and could run from webhooks — neither has the tenant query-filter context.
        var subId = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.CompanyId == companyId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (subId is null)
            return;

        // Increment in the database itself. A read-modify-write (`sub.MessagesUsedThisPeriod++`) loses
        // updates when a campaign batch and inbox sends run concurrently; a single UPDATE ... SET x = x + n
        // is atomic and race-free.
        await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.Id == subId.Value)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    s => s.MessagesUsedThisPeriod, s => s.MessagesUsedThisPeriod + count),
                ct);
    }
}
