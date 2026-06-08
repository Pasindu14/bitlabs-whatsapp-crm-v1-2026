using wa_api.Features.Auth;

namespace wa_api.Features.Users.Dtos;

/// <summary>
/// Public shape of a platform user. NEVER includes the password hash.
/// </summary>
public record UserResponse(
    Guid Id,
    Guid? CompanyId,
    string? CompanyName,
    string FullName,
    string Email,
    UserRole Role,
    IReadOnlyList<string> Permissions,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt
);
