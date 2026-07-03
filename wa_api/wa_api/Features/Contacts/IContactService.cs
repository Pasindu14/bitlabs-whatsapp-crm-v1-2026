using wa_api.Features.Contacts.Dtos;

namespace wa_api.Features.Contacts;

public interface IContactService
{
    /// <summary>Paged, searchable list of the caller's contacts (newest first by default).</summary>
    Task<(IReadOnlyList<ContactResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        bool? isOptedOut = null, Guid? listId = null, CancellationToken ct = default);

    /// <summary>Loads a single contact by id. Throws if not found / not in the caller's company.</summary>
    Task<ContactResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a contact. Throws on duplicate phone within the company.</summary>
    Task<ContactResponse> CreateAsync(CreateContactRequest request, CancellationToken ct = default);

    /// <summary>Bulk-imports contacts: normalizes to E.164, validates, de-dupes (in-file + existing),
    /// inserts the survivors, and returns a summary of what was imported vs skipped.</summary>
    Task<ImportContactsResult> ImportAsync(ImportContactsRequest request, CancellationToken ct = default);

    /// <summary>Updates name/phone. Throws on duplicate phone (excluding self).</summary>
    Task<ContactResponse> UpdateAsync(Guid id, UpdateContactRequest request, CancellationToken ct = default);

    /// <summary>Marks a contact active (IsActive = true). Throws if not found.</summary>
    Task<ContactResponse> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marks a contact inactive (IsActive = false). Throws if not found.</summary>
    Task<ContactResponse> DeactivateAsync(Guid id, CancellationToken ct = default);
}
