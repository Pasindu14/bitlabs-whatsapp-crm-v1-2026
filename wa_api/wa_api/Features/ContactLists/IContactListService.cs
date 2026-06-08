using wa_api.Features.ContactLists.Dtos;

namespace wa_api.Features.ContactLists;

public interface IContactListService
{
    /// <summary>Paged, searchable list of the company's contact lists (with member counts).</summary>
    Task<(IReadOnlyList<ContactListResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>Loads a single list by id (with member count). Throws if not found.</summary>
    Task<ContactListResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a list. Throws on duplicate name within the company.</summary>
    Task<ContactListResponse> CreateAsync(CreateContactListRequest request, CancellationToken ct = default);

    /// <summary>Updates name/description. Throws on duplicate name (excluding self).</summary>
    Task<ContactListResponse> UpdateAsync(Guid id, UpdateContactListRequest request, CancellationToken ct = default);

    /// <summary>Marks a list active (IsActive = true). Throws if not found.</summary>
    Task<ContactListResponse> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marks a list inactive (IsActive = false). Throws if not found.</summary>
    Task<ContactListResponse> DeactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds existing contacts to a list. Skips ids outside the caller's company and ids already
    /// in the list. Returns the refreshed list (with the new member count).
    /// </summary>
    Task<ContactListResponse> AddContactsAsync(Guid id, IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default);

    /// <summary>Removes a contact from a list (hard-deletes the membership; keeps the contact).</summary>
    Task RemoveContactAsync(Guid id, Guid contactId, CancellationToken ct = default);

    /// <summary>
    /// Imports contacts from an <c>.xlsx</c> stream (columns <c>Phone</c>, <c>Name</c>) into a list:
    /// upserts each contact by (company, phone) and adds it to the list. Returns a per-row report.
    /// </summary>
    Task<ImportContactsResult> ImportContactsAsync(Guid id, Stream fileStream, CancellationToken ct = default);
}
