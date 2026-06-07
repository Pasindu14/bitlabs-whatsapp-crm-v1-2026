using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.ContactLists.Dtos;

/// <summary>Payload for <c>PUT /api/v1/contact-lists/{id}</c>.</summary>
public record UpdateContactListRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(500)] string? Description
);
