using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Auth;
using wa_api.Features.Users.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Users;

/// <summary>
/// SuperAdmin-only management of tenant users (CompanyAdmin / Agent). SuperAdmin accounts
/// are excluded from every operation here — they are seeded, not managed through this surface.
/// </summary>
public class UserService(AppDbContext db) : IUserService
{
    public async Task<(IReadOnlyList<UserResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Tenant users only — never surface the platform SuperAdmin in this list.
        var query = db.Users.AsNoTracking().Where(u => u.Role != UserRole.SuperAdmin);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, term) ||
                EF.Functions.ILike(u.Email, term) ||
                (u.Company != null && EF.Functions.ILike(u.Company.Name, term)));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "fullname" or "name" => desc ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName),
            "email" => desc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            _ => desc ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserResponse(
                u.Id,
                u.CompanyId,
                u.Company != null ? u.Company.Name : null,
                u.FullName,
                u.Email,
                u.Role,
                u.IsActive,
                u.LastLoginAt,
                u.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.SuperAdmin, ct)
            ?? throw new NotFoundException("User", id);
        return Map(user);
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var role = request.Role ?? UserRole.CompanyAdmin;
        EnsureTenantRole(role);

        var email = request.Email.Trim().ToLowerInvariant();
        var fullName = request.FullName.Trim();

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);
        if (!company.IsActive)
            throw new ConflictException("COMPANY_INACTIVE", "Cannot assign a user to an inactive company.");

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("USER_EMAIL_DUPLICATE", "A user with this email already exists.");

        var user = new User
        {
            CompanyId = request.CompanyId,
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // Reload with company name for the response.
        user.Company = company;
        return Map(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.SuperAdmin, ct)
            ?? throw new NotFoundException("User", id);

        EnsureTenantRole(request.Role);

        var email = request.Email.Trim().ToLowerInvariant();
        var fullName = request.FullName.Trim();

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);
        if (!company.IsActive)
            throw new ConflictException("COMPANY_INACTIVE", "Cannot assign a user to an inactive company.");

        // Uniqueness check must exclude the row being edited.
        if (await db.Users.AnyAsync(u => u.Id != id && u.Email == email, ct))
            throw new ConflictException("USER_EMAIL_DUPLICATE", "A user with this email already exists.");

        user.CompanyId = request.CompanyId;
        user.Company = company;
        user.FullName = fullName;
        user.Email = email;
        user.Role = request.Role;

        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<UserResponse> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.SuperAdmin, ct)
            ?? throw new NotFoundException("User", id);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public Task<UserResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<UserResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<UserResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.SuperAdmin, ct)
            ?? throw new NotFoundException("User", id);

        user.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    private static void EnsureTenantRole(UserRole role)
    {
        if (role is not (UserRole.CompanyAdmin or UserRole.Agent))
            throw new ConflictException("USER_ROLE_INVALID", "Only CompanyAdmin or Agent users can be managed here.");
    }

    private static UserResponse Map(User u)
        => new(u.Id, u.CompanyId, u.Company?.Name, u.FullName, u.Email, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt);
}
