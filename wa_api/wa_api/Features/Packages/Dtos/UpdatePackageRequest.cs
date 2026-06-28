using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Packages.Dtos;

/// <summary>Payload for <c>PUT /api/v1/packages/{id}</c> (SuperAdmin only).</summary>
public record UpdatePackageRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(500)] string? Description,
    [Range(1, int.MaxValue)] int ExtraMessages,
    [Range(0, 1_000_000)] decimal Price,
    [StringLength(3, MinimumLength = 3)] string? Currency
);
