using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.ContactLists.Dtos;
using wa_api.Features.ContactLists.Entities;
using wa_api.Features.Contacts.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.ContactLists;

public class ContactListService(AppDbContext db) : IContactListService
{
    public async Task<(IReadOnlyList<ContactListResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Global query filter already scopes this to the caller's company.
        var query = db.ContactLists.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.Name, term) ||
                (l.Description != null && EF.Functions.ILike(l.Description, term)));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => desc ? query.OrderByDescending(l => l.Name) : query.OrderBy(l => l.Name),
            _ => desc ? query.OrderByDescending(l => l.CreatedAt) : query.OrderBy(l => l.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new ContactListResponse(
                l.Id, l.Name, l.Description,
                // Member count — the global filter scopes members to this company too.
                db.ContactListMembers.Count(m => m.ContactListId == l.Id),
                l.IsActive, l.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ContactListResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var list = await db.ContactLists.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new ContactListResponse(
                l.Id, l.Name, l.Description,
                db.ContactListMembers.Count(m => m.ContactListId == l.Id),
                l.IsActive, l.CreatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("ContactList", id);
        return list;
    }

    public async Task<ContactListResponse> CreateAsync(CreateContactListRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        // The query filter scopes this to the caller's company, so uniqueness is per-company.
        if (await db.ContactLists.AnyAsync(l => l.Name == name, ct))
            throw new ConflictException("CONTACT_LIST_NAME_DUPLICATE",
                "A contact list with this name already exists.");

        var list = new ContactList
        {
            Name = name,
            Description = Normalize(request.Description),
            // CompanyId auto-stamped from tenant context by AuditInterceptor.
        };
        db.ContactLists.Add(list);
        await db.SaveChangesAsync(ct);

        return new ContactListResponse(list.Id, list.Name, list.Description, 0, list.IsActive, list.CreatedAt);
    }

    public async Task<ContactListResponse> UpdateAsync(Guid id, UpdateContactListRequest request, CancellationToken ct = default)
    {
        var list = await db.ContactLists.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("ContactList", id);

        var name = request.Name.Trim();

        if (await db.ContactLists.AnyAsync(l => l.Id != id && l.Name == name, ct))
            throw new ConflictException("CONTACT_LIST_NAME_DUPLICATE",
                "A contact list with this name already exists.");

        list.Name = name;
        list.Description = Normalize(request.Description);
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public Task<ContactListResponse> ActivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, true, ct);

    public Task<ContactListResponse> DeactivateAsync(Guid id, CancellationToken ct = default)
        => SetActiveAsync(id, false, ct);

    private async Task<ContactListResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var list = await db.ContactLists.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("ContactList", id);
        list.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<ContactListResponse> AddContactsAsync(Guid id, IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default)
    {
        _ = await db.ContactLists.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("ContactList", id);

        var ids = contactIds.Distinct().ToList();

        // Only contacts in the caller's company survive the global filter — cross-tenant ids vanish.
        var validIds = await db.Contacts
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        var alreadyIn = await db.ContactListMembers
            .Where(m => m.ContactListId == id && validIds.Contains(m.ContactId))
            .Select(m => m.ContactId)
            .ToListAsync(ct);

        var toAdd = validIds.Except(alreadyIn).ToList();
        foreach (var contactId in toAdd)
            db.ContactListMembers.Add(new ContactListMember { ContactListId = id, ContactId = contactId });

        if (toAdd.Count > 0)
            await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task RemoveContactAsync(Guid id, Guid contactId, CancellationToken ct = default)
    {
        var member = await db.ContactListMembers
            .FirstOrDefaultAsync(m => m.ContactListId == id && m.ContactId == contactId, ct)
            ?? throw new NotFoundException("ContactListMember", contactId);

        db.ContactListMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ImportContactsResult> ImportContactsAsync(Guid id, Stream fileStream, CancellationToken ct = default)
    {
        _ = await db.ContactLists.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("ContactList", id);

        List<(int Row, string? Phone, string? Name)> rows;
        try
        {
            rows = ParseWorkbook(fileStream);
        }
        catch (FormatException fe)
        {
            throw new BusinessRuleException("IMPORT_MISSING_COLUMNS", fe.Message);
        }
        catch (Exception)
        {
            throw new BusinessRuleException("IMPORT_INVALID_FILE",
                "The file could not be read. Upload a valid .xlsx with 'Phone' and 'Name' columns.");
        }

        var skipped = new List<ImportSkippedRow>();
        var resolved = new List<Contact>();
        int imported = 0, updated = 0;

        // Load this company's contacts once (the global filter scopes them) for phone upsert.
        var existing = await db.Contacts.ToDictionaryAsync(c => c.Phone, c => c, ct);
        var seenPhones = new HashSet<string>();

        foreach (var (rowNum, rawPhone, rawName) in rows)
        {
            var phone = new string((rawPhone ?? string.Empty).Where(char.IsDigit).ToArray());
            var name = (rawName ?? string.Empty).Trim();

            if (phone.Length < 5)
            {
                skipped.Add(new ImportSkippedRow(rowNum, rawPhone, "Invalid or missing phone number"));
                continue;
            }
            if (name.Length == 0)
            {
                skipped.Add(new ImportSkippedRow(rowNum, phone, "Missing name"));
                continue;
            }
            if (!seenPhones.Add(phone))
            {
                skipped.Add(new ImportSkippedRow(rowNum, phone, "Duplicate row in file"));
                continue;
            }

            if (existing.TryGetValue(phone, out var found))
            {
                if (found.Name != name) { found.Name = name; updated++; }
                resolved.Add(found);
            }
            else
            {
                var contact = new Contact { Phone = phone, Name = name };
                db.Contacts.Add(contact);
                existing[phone] = contact;
                resolved.Add(contact);
                imported++;
            }
        }

        // Persist new/updated contacts so new rows get ids + CompanyId before we link them.
        await db.SaveChangesAsync(ct);

        var ids = resolved.Select(c => c.Id).Distinct().ToList();
        var already = await db.ContactListMembers
            .Where(m => m.ContactListId == id && ids.Contains(m.ContactId))
            .Select(m => m.ContactId)
            .ToListAsync(ct);

        var toAdd = ids.Except(already).ToList();
        foreach (var contactId in toAdd)
            db.ContactListMembers.Add(new ContactListMember { ContactListId = id, ContactId = contactId });
        if (toAdd.Count > 0)
            await db.SaveChangesAsync(ct);

        return new ImportContactsResult(rows.Count, imported, updated, toAdd.Count, skipped.Count, skipped);
    }

    /// <summary>Reads the first worksheet, mapping the 'Phone' and 'Name' header columns to row values.</summary>
    private static List<(int Row, string? Phone, string? Name)> ParseWorkbook(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault()
            ?? throw new FormatException("The workbook has no worksheets.");

        var headerRow = ws.FirstRowUsed()
            ?? throw new FormatException("The sheet is empty.");

        int phoneCol = 0, nameCol = 0;
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim().ToLowerInvariant();
            if (header == "phone") phoneCol = cell.Address.ColumnNumber;
            else if (header == "name") nameCol = cell.Address.ColumnNumber;
        }
        if (phoneCol == 0 || nameCol == 0)
            throw new FormatException("The file must have 'Phone' and 'Name' header columns.");

        var rows = new List<(int, string?, string?)>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var phoneCell = row.Cell(phoneCol);
            // Numeric phone cells would stringify in scientific notation — read them as integers.
            var phone = phoneCell.DataType == XLDataType.Number
                ? ((long)phoneCell.GetDouble()).ToString()
                : phoneCell.GetString();
            var name = row.Cell(nameCol).GetString();

            if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(name))
                continue; // skip fully-blank rows

            rows.Add((r, phone, name));
        }
        return rows;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
