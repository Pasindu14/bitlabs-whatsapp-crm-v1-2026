using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.WhatsApp.Dtos;

/// <summary>
/// A WABA connection as returned by the list/detail/create/update endpoints.
/// The access token is NEVER included — only <see cref="HasAccessToken"/> signals presence.
/// </summary>
public record WabaConnectionResponse(
    Guid Id,
    Guid CompanyId,
    string? CompanyName,
    string PhoneNumberId,
    string WabaId,
    string DisplayPhoneNumber,
    WabaConnectionStatus Status,
    bool HasAccessToken,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastHealthCheckAt,
    string? HealthCheckErrorMessage,
    /// <summary>Meta quality rating (GREEN/YELLOW/RED). Null until first synced from Meta.</summary>
    string? QualityRating,
    /// <summary>Meta messaging limit tier (24-hour unique-recipient cap).</summary>
    MessagingTier MessagingTier,
    /// <summary>Unique business-initiated recipients sent today, counted toward the tier cap (UTC day).</summary>
    int DailySentToday
);
