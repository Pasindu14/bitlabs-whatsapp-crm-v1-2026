using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.ContactLists.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/contact-lists/{id}/contacts</c> — add one or more EXISTING
/// contacts to a list. Ids not belonging to the caller's company, or already in the list,
/// are silently skipped.
/// </summary>
public record AddContactsToListRequest(
    [Required, MinLength(1)] Guid[] ContactIds
);
