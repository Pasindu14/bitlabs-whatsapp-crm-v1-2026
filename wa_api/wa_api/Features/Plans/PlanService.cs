using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Auth;
using wa_api.Features.Plans.Dtos;
using wa_api.Features.Plans.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Plans;

public class PlanService(AppDbContext db) : IPlanService
{
    public async Task<(IReadOnlyList<PlanResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Platform catalog: no tenant filter. SuperAdmin sees every plan (active + inactive).
        var query = db.Plans.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, term));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => desc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "price" => desc ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            "quota" => desc ? query.OrderByDescending(p => p.MonthlyMessageQuota) : query.OrderBy(p => p.MonthlyMessageQuota),
            _ => desc ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items.Select(Map).ToList(), total);
    }

    public async Task<PlanResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Plan", id);
        return Map(plan);
    }

    public async Task<PlanResponse> CreateAsync(CreatePlanRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var flags = NormalizeFeatureFlags(request.FeatureFlags);

        if (await db.Plans.AnyAsync(p => p.Name == name, ct))
            throw new ConflictException("PLAN_NAME_DUPLICATE", "A plan with this name already exists.");

        var plan = new Plan
        {
            Name = name,
            MonthlyMessageQuota = request.MonthlyMessageQuota,
            Price = request.Price,
            Currency = NormalizeCurrency(request.Currency),
            FeatureFlags = flags,
        };
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);

        return Map(plan);
    }

    public async Task<PlanResponse> UpdateAsync(Guid id, UpdatePlanRequest request, CancellationToken ct = default)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Plan", id);

        var name = request.Name.Trim();
        var flags = NormalizeFeatureFlags(request.FeatureFlags);

        // Uniqueness on name must exclude the row being edited.
        if (await db.Plans.AnyAsync(p => p.Id != id && p.Name == name, ct))
            throw new ConflictException("PLAN_NAME_DUPLICATE", "A plan with this name already exists.");

        plan.Name = name;
        plan.MonthlyMessageQuota = request.MonthlyMessageQuota;
        plan.Price = request.Price;
        plan.Currency = NormalizeCurrency(request.Currency);
        plan.FeatureFlags = flags;

        await db.SaveChangesAsync(ct);

        return Map(plan);
    }

    public Task<PlanResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<PlanResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<PlanResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Plan", id);

        plan.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return Map(plan);
    }

    /// <summary>Trims, dedupes, and validates feature-flag keys against the permission catalog.</summary>
    private static List<string> NormalizeFeatureFlags(IReadOnlyList<string>? flags)
    {
        if (flags is null || flags.Count == 0)
            return [];

        var cleaned = flags
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!Permission.AreAllValid(cleaned))
        {
            var unknown = cleaned.Where(f => !Permission.All.Contains(f)).ToArray();
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(CreatePlanRequest.FeatureFlags)] =
                    [$"Unknown feature flag(s): {string.Join(", ", unknown)}."]
            });
        }

        return cleaned;
    }

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

    private static PlanResponse Map(Plan p)
        => new(p.Id, p.Name, p.MonthlyMessageQuota, p.Price, p.Currency, p.FeatureFlags, p.IsActive, p.CreatedAt);
}
