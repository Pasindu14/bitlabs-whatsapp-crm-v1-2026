using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.Tenancy;
using wa_api.Features.Packages.Dtos;
using wa_api.Features.Packages.Entities;
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

        var now = DateTime.UtcNow;
        var periodDays = request.PeriodDays ?? DefaultPeriodDays;

        // Re-subscribe is "stack or fresh" (no monthly reset — the balance runs until expiry):
        //  • STACK  — if the company already has a LIVE subscription (not expired AND messages
        //             remaining), the new plan's quota is added to the running balance and the
        //             expiry is extended from the CURRENT expiry. The existing row is kept.
        //  • FRESH  — otherwise (no sub, expired, or balance exhausted) any leftover is forfeited
        //             and a brand-new period starts from today.
        var existing = await LoadActiveAsync(company.Id, asNoTracking: false, ct);
        if (existing is not null && IsLive(existing, now))
        {
            // Stack the new plan's quota onto the running balance. We keep the existing PlanId as
            // the base and fold the purchased quota into ExtraMessageCredits so the remaining
            // balance rises by exactly the new quota (remaining = base + credits - used).
            existing.ExtraMessageCredits += plan.MonthlyMessageQuota;
            existing.CurrentPeriodEnd = existing.CurrentPeriodEnd.AddDays(periodDays);
            await db.SaveChangesAsync(ct);
            return Map(existing);
        }

        // FRESH: cancel any existing active subscription FIRST (own SaveChanges) so the unique
        // filtered index on (CompanyId WHERE Status='Active') never sees two active rows.
        await CancelExistingActiveAsync(company.Id, ct);

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

    /// <summary>
    /// A subscription is "live" (and therefore stackable) when it has not expired AND still has
    /// messages remaining. If either is false the customer must start a fresh period.
    /// </summary>
    private static bool IsLive(Subscription sub, DateTime now)
    {
        var effective = (sub.Plan?.MonthlyMessageQuota ?? 0) + sub.ExtraMessageCredits;
        var remaining = effective - sub.MessagesUsedThisPeriod;
        return sub.CurrentPeriodEnd > now && remaining > 0;
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

    public async Task<SubscriptionResponse> AddPackageAsync(Guid companyId, AddPackageRequest request, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("Company", companyId);

        var package = await db.MessagePackages.FirstOrDefaultAsync(p => p.Id == request.PackageId, ct)
            ?? throw new NotFoundException("Package", request.PackageId);

        if (!package.IsActive)
            throw new BusinessRuleException("PACKAGE_INACTIVE", "The selected package is inactive and cannot be added.");

        var sub = await LoadActiveAsync(companyId, asNoTracking: false, ct)
            ?? throw new BusinessRuleException("SUBSCRIPTION_INACTIVE",
                "This company has no active subscription to add a package to. Assign a plan first.");

        // Top up the credit balance and record the purchase (snapshotting the package fields so
        // later catalog edits don't rewrite history).
        sub.ExtraMessageCredits += package.ExtraMessages;

        db.PackagePurchases.Add(new PackagePurchase
        {
            CompanyId = company.Id,          // explicit — SuperAdmin scope has no tenant context
            SubscriptionId = sub.Id,
            PackageId = package.Id,
            PackageName = package.Name,
            MessagesAdded = package.ExtraMessages,
            Price = package.Price,
            Currency = package.Currency,
        });

        await db.SaveChangesAsync(ct);

        return Map(sub);
    }

    public async Task<IReadOnlyList<PackagePurchaseResponse>> GetPackageHistoryAsync(Guid companyId, CancellationToken ct = default)
    {
        _ = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new NotFoundException("Company", companyId);

        // SuperAdmin path: query filter bypassed, so scope by the explicit CompanyId predicate.
        var rows = await db.PackagePurchases.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(PackagePurchaseResponse.From).ToList();
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
        var baseQuota = s.Plan?.MonthlyMessageQuota ?? 0;
        var effective = baseQuota + s.ExtraMessageCredits;
        var remaining = Math.Max(0, effective - s.MessagesUsedThisPeriod);
        return new SubscriptionResponse(
            s.Id, s.CompanyId, s.Company?.Name, s.PlanId, s.Plan?.Name, s.Status,
            baseQuota, s.ExtraMessageCredits, effective, s.MessagesUsedThisPeriod, remaining,
            s.CurrentPeriodStart, s.CurrentPeriodEnd, s.IsActive, true, s.CreatedAt);
    }
}
