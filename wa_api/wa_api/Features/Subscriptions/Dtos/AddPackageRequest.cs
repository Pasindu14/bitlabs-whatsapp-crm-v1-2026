using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Subscriptions.Dtos;

/// <summary>
/// Payload for <c>POST /api/v1/subscriptions/company/{companyId}/packages</c> (SuperAdmin only).
/// Adds a message-credit package's <c>ExtraMessages</c> to the company's active subscription.
/// </summary>
public record AddPackageRequest(
    [Required] Guid PackageId
);
