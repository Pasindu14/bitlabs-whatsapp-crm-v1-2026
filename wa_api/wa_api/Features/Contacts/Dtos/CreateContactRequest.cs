using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Contacts.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/contacts</c>. The owning company is resolved from the
/// caller's JWT — it is NEVER accepted from the body (cross-tenant safety).
/// </summary>
public record CreateContactRequest(
    [Required, StringLength(20, MinimumLength = 5)] string Phone,
    [Required, StringLength(200, MinimumLength = 1)] string Name
);
