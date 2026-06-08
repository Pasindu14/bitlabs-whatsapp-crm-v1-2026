using System.ComponentModel.DataAnnotations;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.WhatsApp.Dtos;

/// <summary>
/// Payload for <c>PUT /api/v1/waba-connections/{id}</c> (SuperAdmin only).
/// <see cref="AccessToken"/> is optional: leave it blank to keep the stored token.
/// </summary>
public record UpdateWabaConnectionRequest(
    [Required] Guid CompanyId,
    [Required, StringLength(64, MinimumLength = 1)] string PhoneNumberId,
    [Required, StringLength(64, MinimumLength = 1)] string WabaId,
    [StringLength(32)] string? DisplayPhoneNumber,
    // Blank = keep the existing token; non-blank = replace it.
    [StringLength(2048)] string? AccessToken,
    WabaConnectionStatus? Status
);
