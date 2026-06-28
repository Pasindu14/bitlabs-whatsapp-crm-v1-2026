using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Packages.Dtos;
using wa_api.Features.Packages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Packages;

public class PackageService(AppDbContext db) : IPackageService
{
    public async Task<(IReadOnlyList<PackageResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Platform catalog: no tenant filter. SuperAdmin sees every package (active + inactive).
        var query = db.MessagePackages.AsNoTracking();

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
            "messages" => desc ? query.OrderByDescending(p => p.ExtraMessages) : query.OrderBy(p => p.ExtraMessages),
            _ => desc ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items.Select(PackageResponse.From).ToList(), total);
    }

    public async Task<PackageResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var pkg = await db.MessagePackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Package", id);
        return PackageResponse.From(pkg);
    }

    public async Task<PackageResponse> CreateAsync(CreatePackageRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (await db.MessagePackages.AnyAsync(p => p.Name == name, ct))
            throw new ConflictException("PACKAGE_NAME_DUPLICATE", "A package with this name already exists.");

        var pkg = new MessagePackage
        {
            Name = name,
            Description = request.Description?.Trim(),
            ExtraMessages = request.ExtraMessages,
            Price = request.Price,
            Currency = NormalizeCurrency(request.Currency),
        };
        db.MessagePackages.Add(pkg);
        await db.SaveChangesAsync(ct);

        return PackageResponse.From(pkg);
    }

    public async Task<PackageResponse> UpdateAsync(Guid id, UpdatePackageRequest request, CancellationToken ct = default)
    {
        var pkg = await db.MessagePackages.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Package", id);

        var name = request.Name.Trim();

        if (await db.MessagePackages.AnyAsync(p => p.Id != id && p.Name == name, ct))
            throw new ConflictException("PACKAGE_NAME_DUPLICATE", "A package with this name already exists.");

        pkg.Name = name;
        pkg.Description = request.Description?.Trim();
        pkg.ExtraMessages = request.ExtraMessages;
        pkg.Price = request.Price;
        pkg.Currency = NormalizeCurrency(request.Currency);

        await db.SaveChangesAsync(ct);

        return PackageResponse.From(pkg);
    }

    public Task<PackageResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<PackageResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<PackageResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var pkg = await db.MessagePackages.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Package", id);

        pkg.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return PackageResponse.From(pkg);
    }

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
}
