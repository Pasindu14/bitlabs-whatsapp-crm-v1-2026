using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Contacts.Dtos;
using wa_api.Features.Contacts.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Contacts;

public class ContactService(AppDbContext db) : IContactService
{
    public async Task<(IReadOnlyList<ContactResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        bool? isOptedOut = null, Guid? listId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Global query filter already scopes this to the caller's company.
        var query = db.Contacts.AsNoTracking();

        // Optional: only contacts that are members of the given list.
        if (listId is { } lid)
            query = query.Where(c => db.ContactListMembers.Any(m => m.ContactListId == lid && m.ContactId == c.Id));

        // Optional: filter by opt-out (suppression) status.
        if (isOptedOut is { } optedOut)
            query = query.Where(c => c.IsOptedOut == optedOut);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Phone, term) ||
                EF.Functions.ILike(c.Name, term));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "phone" => desc ? query.OrderByDescending(c => c.Phone) : query.OrderBy(c => c.Phone),
            _ => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ContactResponse(c.Id, c.Phone, c.Name, c.IsActive,
                c.IsOptedOut, c.OptedOutAt, c.HasOptedIn, c.OptedInAt, c.ConsentSource, c.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ContactResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contact", id);
        return Map(contact);
    }

    public async Task<ContactResponse> CreateAsync(CreateContactRequest request, CancellationToken ct = default)
    {
        var phone = NormalizePhone(request.Phone);

        // The query filter scopes this to the caller's company, so uniqueness is per-company.
        if (await db.Contacts.AnyAsync(c => c.Phone == phone, ct))
            throw new ConflictException("CONTACT_PHONE_DUPLICATE",
                "A contact with this phone number already exists.");

        var contact = new Contact
        {
            Phone = phone,
            Name = request.Name.Trim(),
            HasOptedIn = request.HasOptedIn,
            OptedInAt = request.HasOptedIn ? DateTime.UtcNow : null,
            // Manual opt-in at creation attests to off-platform consent proof.
            ConsentSource = request.HasOptedIn ? ConsentSource.ManualEntry : ConsentSource.None,
            // CompanyId is auto-stamped from tenant context by AuditInterceptor on insert.
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        return Map(contact);
    }

    public async Task<ContactResponse> UpdateAsync(Guid id, UpdateContactRequest request, CancellationToken ct = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contact", id);

        var phone = NormalizePhone(request.Phone);

        // Uniqueness on phone must exclude the row being edited.
        if (await db.Contacts.AnyAsync(c => c.Id != id && c.Phone == phone, ct))
            throw new ConflictException("CONTACT_PHONE_DUPLICATE",
                "A contact with this phone number already exists.");

        contact.Phone = phone;
        contact.Name = request.Name.Trim();

        if (request.HasOptedIn.HasValue)
        {
            if (request.HasOptedIn.Value && !contact.HasOptedIn)
            {
                contact.HasOptedIn = true;
                contact.OptedInAt = DateTime.UtcNow;
                contact.ConsentSource = ConsentSource.ManualEntry;
            }
            else if (!request.HasOptedIn.Value)
            {
                contact.HasOptedIn = false;
                contact.ConsentSource = ConsentSource.None;
            }
        }

        // Company admin can manually flip suppression — the only sanctioned way to lift a STOP. Applied
        // AFTER HasOptedIn so that if both arrive in one request, opt-out wins (a suppressed contact is
        // never messaged regardless of consent).
        if (request.IsOptedOut.HasValue && request.IsOptedOut.Value != contact.IsOptedOut)
        {
            if (request.IsOptedOut.Value)
            {
                // Mirror the webhook STOP path exactly.
                contact.IsOptedOut = true;
                contact.OptedOutAt = DateTime.UtcNow;
                contact.HasOptedIn = false;
                contact.ConsentSource = ConsentSource.None;
            }
            else
            {
                // Lift a mistaken opt-out: re-enable sending. Does NOT by itself grant consent.
                contact.IsOptedOut = false;
                contact.OptedOutAt = null;
            }
        }

        await db.SaveChangesAsync(ct);

        return Map(contact);
    }

    public Task<ContactResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<ContactResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<ContactResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contact", id);
        contact.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return Map(contact);
    }

    /// <summary>Digits only — strips '+', spaces and separators (e.g. "+94 77 123 4567" → "94771234567").</summary>
    private static string NormalizePhone(string phone)
        => new(phone.Where(char.IsDigit).ToArray());

    private static ContactResponse Map(Contact c)
        => new(c.Id, c.Phone, c.Name, c.IsActive, c.IsOptedOut, c.OptedOutAt, c.HasOptedIn, c.OptedInAt, c.ConsentSource, c.CreatedAt);
}
