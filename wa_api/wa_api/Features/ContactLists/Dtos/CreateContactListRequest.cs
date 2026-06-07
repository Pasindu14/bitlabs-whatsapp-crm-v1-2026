using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.ContactLists.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/contact-lists</c>. CompanyId is resolved from the JWT — never sent.
/// </summary>
public record CreateContactListRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(500)] string? Description
);
