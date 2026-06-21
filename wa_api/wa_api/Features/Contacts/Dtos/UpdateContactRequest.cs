using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Contacts.Dtos;

/// <summary>Payload for <c>PUT /api/v1/contacts/{id}</c>.</summary>
public record UpdateContactRequest(
    [Required, StringLength(20, MinimumLength = 5)] string Phone,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    bool? HasOptedIn = null
);
