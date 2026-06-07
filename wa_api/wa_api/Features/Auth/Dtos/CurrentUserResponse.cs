namespace wa_api.Features.Auth.Dtos;

/// <summary>
/// Safe projection of a <see cref="User"/> — never exposes the password hash.
/// Returned by <c>GET /api/v1/auth/me</c> and embedded in <see cref="AuthResponse"/>.
/// </summary>
public record CurrentUserResponse(
    Guid Id,
    string Email,
    string FullName,
    UserRole Role,
    IReadOnlyList<string> Permissions,
    Guid? CompanyId
);
