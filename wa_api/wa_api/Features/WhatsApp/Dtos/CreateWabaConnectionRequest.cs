using System.ComponentModel.DataAnnotations;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.WhatsApp.Dtos;

/// <summary>Payload for <c>POST /api/v1/waba-connections</c> (SuperAdmin only).</summary>
public record CreateWabaConnectionRequest(
    [Required] Guid CompanyId,
    [Required, StringLength(64, MinimumLength = 1)] string PhoneNumberId,
    [Required, StringLength(64, MinimumLength = 1)] string WabaId,
    [StringLength(32)] string? DisplayPhoneNumber,
    [Required, StringLength(2048, MinimumLength = 1)] string AccessToken,
    WabaConnectionStatus? Status
);
