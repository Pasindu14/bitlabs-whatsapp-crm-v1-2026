using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Companies.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Companies;

public class CompanyService(AppDbContext db) : ICompanyService
{
    public async Task<(IReadOnlyList<CompanyResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Soft-delete is a status, not a filter here: SuperAdmin sees every company
        // (active + inactive) so deactivated tenants remain visible/restorable.
        var query = db.Companies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, term) ||
                (c.Email != null && EF.Functions.ILike(c.Email, term)) ||
                (c.Slug != null && EF.Functions.ILike(c.Slug, term)));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "email" => desc ? query.OrderByDescending(c => c.Email) : query.OrderBy(c => c.Email),
            _ => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CompanyResponse(
                c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<CompanyResponse> CreateAsync(CreateCompanyRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? null : request.Slug.Trim().ToLowerInvariant();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        if (await db.Companies.AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct))
            throw new DuplicateResourceException("Company");

        if (slug is not null && await db.Companies.AnyAsync(c => c.Slug == slug, ct))
            throw new ConflictException("COMPANY_SLUG_DUPLICATE", "A company with this slug already exists.");

        var company = new Company { Name = name, Slug = slug, Email = email, Phone = phone };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);

        return new CompanyResponse(
            company.Id, company.Name, company.Slug, company.Email, company.Phone,
            company.IsActive, company.CreatedAt);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Company", id);

        company.IsActive = false;   // soft-delete (platform convention)
        await db.SaveChangesAsync(ct);
    }
}
