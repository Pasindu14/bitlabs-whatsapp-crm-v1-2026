using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.WhatsApp.Dtos;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.WhatsApp;

public class WabaConnectionService(AppDbContext db) : IWabaConnectionService
{
    public async Task<(IReadOnlyList<WabaConnectionResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // SuperAdmin sees every connection (active + inactive) so deactivated rows stay visible.
        var query = db.WabaConnections.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(w =>
                EF.Functions.ILike(w.PhoneNumberId, term) ||
                EF.Functions.ILike(w.WabaId, term) ||
                EF.Functions.ILike(w.DisplayPhoneNumber, term) ||
                EF.Functions.ILike(w.Company.Name, term));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "company" => desc ? query.OrderByDescending(w => w.Company.Name) : query.OrderBy(w => w.Company.Name),
            "displayphonenumber" => desc ? query.OrderByDescending(w => w.DisplayPhoneNumber) : query.OrderBy(w => w.DisplayPhoneNumber),
            "status" => desc ? query.OrderByDescending(w => w.Status) : query.OrderBy(w => w.Status),
            _ => desc ? query.OrderByDescending(w => w.CreatedAt) : query.OrderBy(w => w.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WabaConnectionResponse(
                w.Id, w.CompanyId, w.Company.Name, w.PhoneNumberId, w.WabaId,
                w.DisplayPhoneNumber, w.Status, w.EncryptedAccessToken != "", w.IsActive, w.CreatedAt,
                w.LastHealthCheckAt, w.HealthCheckErrorMessage))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<WabaConnectionResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var conn = await db.WabaConnections.AsNoTracking()
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("WabaConnection", id);
        return Map(conn);
    }

    public async Task<WabaConnectionResponse> CreateAsync(CreateWabaConnectionRequest request, CancellationToken ct = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct)
            ?? throw new NotFoundException("Company", request.CompanyId);

        var phoneNumberId = request.PhoneNumberId.Trim();
        var wabaId = request.WabaId.Trim();

        if (await db.WabaConnections.AnyAsync(w => w.PhoneNumberId == phoneNumberId, ct))
            throw new ConflictException("WABA_PHONE_NUMBER_DUPLICATE",
                "A connection with this phone number id already exists.");

        var conn = new WabaConnection
        {
            CompanyId = company.Id,
            PhoneNumberId = phoneNumberId,
            WabaId = wabaId,
            DisplayPhoneNumber = Normalize(request.DisplayPhoneNumber) ?? string.Empty,
            EncryptedAccessToken = request.AccessToken.Trim(),
            Status = request.Status ?? WabaConnectionStatus.Connected,
        };
        db.WabaConnections.Add(conn);
        await db.SaveChangesAsync(ct);

        return Map(conn, company.Name);
    }

    public async Task<WabaConnectionResponse> UpdateAsync(Guid id, UpdateWabaConnectionRequest request, CancellationToken ct = default)
    {
        var conn = await db.WabaConnections
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("WabaConnection", id);

        var company = conn.CompanyId == request.CompanyId
            ? conn.Company
            : await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId, ct)
                ?? throw new NotFoundException("Company", request.CompanyId);

        var phoneNumberId = request.PhoneNumberId.Trim();

        // Uniqueness on phone-number id must exclude the row being edited.
        if (await db.WabaConnections.AnyAsync(w => w.Id != id && w.PhoneNumberId == phoneNumberId, ct))
            throw new ConflictException("WABA_PHONE_NUMBER_DUPLICATE",
                "A connection with this phone number id already exists.");

        conn.CompanyId = company.Id;
        conn.PhoneNumberId = phoneNumberId;
        conn.WabaId = request.WabaId.Trim();
        conn.DisplayPhoneNumber = Normalize(request.DisplayPhoneNumber) ?? string.Empty;
        conn.Status = request.Status ?? conn.Status;

        // Replace the token only when a new (non-blank) one is supplied.
        var newToken = Normalize(request.AccessToken);
        if (newToken is not null)
            conn.EncryptedAccessToken = newToken;

        await db.SaveChangesAsync(ct);

        return Map(conn, company.Name);
    }

    public Task<WabaConnectionResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<WabaConnectionResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<WabaConnectionResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var conn = await db.WabaConnections
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("WabaConnection", id);

        conn.IsActive = isActive;
        conn.Status = isActive ? WabaConnectionStatus.Connected : WabaConnectionStatus.Disconnected;
        await db.SaveChangesAsync(ct);
        return Map(conn);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WabaConnectionResponse Map(WabaConnection w, string? companyName = null)
        => new(
            w.Id, w.CompanyId, companyName ?? w.Company?.Name,
            w.PhoneNumberId, w.WabaId, w.DisplayPhoneNumber, w.Status,
            !string.IsNullOrEmpty(w.EncryptedAccessToken), w.IsActive, w.CreatedAt,
            w.LastHealthCheckAt, w.HealthCheckErrorMessage);
}
