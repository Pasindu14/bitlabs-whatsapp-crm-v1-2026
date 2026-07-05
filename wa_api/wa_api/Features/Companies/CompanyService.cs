using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Auth;
using wa_api.Features.Companies.Dtos;
using wa_api.Features.Users.Dtos;
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

    public async Task<CompanyResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Company", id);
        return Map(company);
    }

    public async Task<CompanyResponse> CreateAsync(CreateCompanyRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var slug = Normalize(request.Slug)?.ToLowerInvariant();
        var email = Normalize(request.Email)?.ToLowerInvariant();
        var phone = Normalize(request.Phone);

        if (await db.Companies.AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct))
            throw new DuplicateResourceException("Company");

        if (slug is not null && await db.Companies.AnyAsync(c => c.Slug == slug, ct))
            throw new ConflictException("COMPANY_SLUG_DUPLICATE", "A company with this slug already exists.");

        var company = new Company { Name = name, Slug = slug, Email = email, Phone = phone };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);

        return Map(company);
    }

    public async Task<ProvisionCompanyResponse> ProvisionAsync(ProvisionCompanyRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        var slug = Normalize(request.Slug)?.ToLowerInvariant();
        var companyEmail = Normalize(request.CompanyEmail)?.ToLowerInvariant();
        var phone = Normalize(request.Phone);
        var adminEmail = request.AdminEmail.Trim().ToLowerInvariant();
        var adminFullName = request.AdminFullName.Trim();

        if (await db.Companies.AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct))
            throw new DuplicateResourceException("Company");

        if (slug is not null && await db.Companies.AnyAsync(c => c.Slug == slug, ct))
            throw new ConflictException("COMPANY_SLUG_DUPLICATE", "A company with this slug already exists.");

        if (await db.Users.AnyAsync(u => u.Email == adminEmail, ct))
            throw new ConflictException("USER_EMAIL_DUPLICATE", "A user with this email already exists.");

        // The DbContext runs an EnableRetryOnFailure execution strategy, which forbids a bare user-initiated
        // BeginTransaction (it must own the whole retriable unit), so the company+admin transaction runs INSIDE
        // strategy.ExecuteAsync. The entities are built inside the delegate and the tracker is cleared at the
        // top of each attempt, so a transient-fault retry re-inserts cleanly instead of re-adding the previous
        // attempt's still-tracked rows.
        Company company = null!;
        User admin = null!;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            company = new Company { Name = name, Slug = slug, Email = companyEmail, Phone = phone };
            db.Companies.Add(company);
            await db.SaveChangesAsync(ct);

            admin = new User
            {
                CompanyId = company.Id,
                FullName = adminFullName,
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
                Role = UserRole.CompanyAdmin,
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

        admin.Company = company;
        return new ProvisionCompanyResponse(Map(company), MapUser(admin));
    }

    public async Task<CompanyResponse> UpdateAsync(Guid id, UpdateCompanyRequest request, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Company", id);

        var name = request.Name.Trim();
        var slug = Normalize(request.Slug)?.ToLowerInvariant();
        var email = Normalize(request.Email)?.ToLowerInvariant();
        var phone = Normalize(request.Phone);

        // Uniqueness checks must exclude the row being edited.
        if (await db.Companies.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower(), ct))
            throw new DuplicateResourceException("Company");

        if (slug is not null && await db.Companies.AnyAsync(c => c.Id != id && c.Slug == slug, ct))
            throw new ConflictException("COMPANY_SLUG_DUPLICATE", "A company with this slug already exists.");

        company.Name = name;
        company.Slug = slug;
        company.Email = email;
        company.Phone = phone;
        await db.SaveChangesAsync(ct);

        return Map(company);
    }

    public Task<CompanyResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<CompanyResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<CompanyResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Company", id);

        company.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return Map(company);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CompanyResponse Map(Company c)
        => new(c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.CreatedAt);

    private static UserResponse MapUser(User u)
        => new(u.Id, u.CompanyId, u.Company?.Name, u.FullName, u.Email, u.Role,
               u.Permissions.AsReadOnly(), u.IsActive, u.LastLoginAt, u.CreatedAt);
}
