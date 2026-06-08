using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Plans.Entities;
using wa_api.Features.Subscriptions.Dtos;
using wa_api.Features.Subscriptions.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Subscriptions;

public class SubscriptionService(AppDbContext db, ITenantContext tenant) : ISubscriptionService
{
    private const int DefaultPeriodDays = 30;

    public async Task<(IReadOnlyList<SubscriptionResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // SuperAdmin path: query filter is bypassed, so this spans all companies. Only ACTIVE
        // subscriptions are listed — this is the "current state" view (one row per company, since
        // the unique filtered index allows at most one active sub per company). Cancelled/expired
        // history is intentionally excluded, which also keeps the row key (companyId) unique.
        var query = db.Subscriptions.AsNoTracking()
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Company.Name, term) ||
                EF.Functions.ILike(s.Plan.Name, term));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "company" => desc ? query.OrderByDescending(s => s.Company.Name) : query.OrderBy(s => s.Company.Name),
            "plan" => desc ? query.OrderByDescending(s => s.Plan.Name) : query.OrderBy(s => s.Plan.Name),
            "status" => desc ? query.OrderByDescending(s => s.Status) : query.OrderBy(s => s.Status),
            _ => desc ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items.Select(Map).ToList(), total);
    }

    public async Task<SubscriptionResponse> GetForCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("Company", companyId);

        var sub = await LoadActiveAsync(companyId, asNoTracking: true, ct);
        return sub is null ? SubscriptionResponse.None(companyId, company.Name) : Map(sub);
    }

    public async Task<SubscriptionResponse> GetMineAsync(CancellationToken ct = default)
    {
        if (tenant.CompanyId is not { } companyId)
            throw new AuthorizationException("subscription");

        // The global query filter already restricts to the caller's company.
        var sub = await db.Subscriptions.AsNoTracking()
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return sub is null ? SubscriptionResponse.None(companyId) : Map(sub);
    }

    public async Task<SubscriptionResponse> AssignAsync(AssignSubscriptionRequest request, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);

        var plan = await RequireAssignablePlanAsync(request.PlanId, ct);

        // Cancel any existing active subscription FIRST (own SaveChanges) so the unique
        // filtered index on (CompanyId WHERE Status='Active') never sees two active rows.
        await CancelExistingActiveAsync(company.Id, ct);

        var now = DateTime.UtcNow;
        var periodDays = request.PeriodDays ?? DefaultPeriodDays;
        var sub = new Subscription
        {
            CompanyId = company.Id,        // explicit — interceptor leaves SuperAdmin-set CompanyId alone
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddDays(periodDays),
            MessagesUsedThisPeriod = 0,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(ct);

        sub.Company = company;
        sub.Plan = plan;
        return Map(sub);
    }

    public async Task<SubscriptionResponse> ChangePlanAsync(Guid companyId, ChangePlanRequest request, CancellationToken ct = default)
    {
        _ = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("Company", companyId);

        var sub = await LoadActiveAsync(companyId, asNoTracking: false, ct)
            ?? throw new NotFoundException("Subscription", companyId);

        var plan = await RequireAssignablePlanAsync(request.PlanId, ct);

        sub.PlanId = plan.Id;

        if (request.ResetPeriod == true)
        {
            var now = DateTime.UtcNow;
            sub.CurrentPeriodStart = now;
            sub.CurrentPeriodEnd = now.AddDays(request.PeriodDays ?? DefaultPeriodDays);
            sub.MessagesUsedThisPeriod = 0;
        }

        await db.SaveChangesAsync(ct);

        sub.Plan = plan;
        return Map(sub);
    }

    public async Task<SubscriptionResponse> CancelAsync(Guid companyId, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("Company", companyId);

        var sub = await LoadActiveAsync(companyId, asNoTracking: false, ct)
            ?? throw new NotFoundException("Subscription", companyId);

        sub.Status = SubscriptionStatus.Cancelled;
        sub.IsActive = false;
        await db.SaveChangesAsync(ct);

        sub.Company = company;
        return Map(sub);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Loads the company's single active subscription (Plan + Company included).</summary>
    private async Task<Subscription?> LoadActiveAsync(Guid companyId, bool asNoTracking, CancellationToken ct)
    {
        // SuperAdmin path: query filter bypassed, so the explicit CompanyId predicate scopes it.
        var query = db.Subscriptions
            .Include(s => s.Company)
            .Include(s => s.Plan)
            .Where(s => s.CompanyId == companyId && s.Status == SubscriptionStatus.Active);

        if (asNoTracking) query = query.AsNoTracking();

        return await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private async Task CancelExistingActiveAsync(Guid companyId, CancellationToken ct)
    {
        var existing = await db.Subscriptions
            .Where(s => s.CompanyId == companyId && s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);

        if (existing.Count == 0) return;

        foreach (var s in existing)
        {
            s.Status = SubscriptionStatus.Cancelled;
            s.IsActive = false;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<Plan> RequireAssignablePlanAsync(Guid planId, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new NotFoundException("Plan", planId);

        if (!plan.IsActive)
            throw new BusinessRuleException("PLAN_INACTIVE", "The selected plan is inactive and cannot be assigned.");

        return plan;
    }

    private static SubscriptionResponse Map(Subscription s)
    {
        var quota = s.Plan?.MonthlyMessageQuota ?? 0;
        var remaining = Math.Max(0, quota - s.MessagesUsedThisPeriod);
        return new SubscriptionResponse(
            s.Id, s.CompanyId, s.Company?.Name, s.PlanId, s.Plan?.Name, s.Status,
            quota, s.MessagesUsedThisPeriod, remaining,
            s.CurrentPeriodStart, s.CurrentPeriodEnd, s.IsActive, true, s.CreatedAt);
    }
}
