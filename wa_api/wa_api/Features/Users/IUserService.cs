using wa_api.Features.Users.Dtos;

namespace wa_api.Features.Users;

public interface IUserService
{
    Task<(IReadOnlyList<UserResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<UserResponse> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    Task<UserResponse> ActivateAsync(Guid id, CancellationToken ct = default);
    Task<UserResponse> DeactivateAsync(Guid id, CancellationToken ct = default);

    // ── Company-scoped surface (CompanyAdmin) ──────────────────────────────
    // Every method below is hard-scoped to the caller's own company (from JWT),
    // so a CompanyAdmin can only ever see/manage users within their company.
    Task<(IReadOnlyList<UserResponse> Items, int Total)> GetPagedForCompanyAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    Task<UserResponse> GetByIdForCompanyAsync(Guid id, CancellationToken ct = default);
    Task<UserResponse> CreateForCompanyAsync(CreateCompanyUserRequest request, CancellationToken ct = default);
    Task<UserResponse> UpdateForCompanyAsync(Guid id, UpdateCompanyUserRequest request, CancellationToken ct = default);
    Task<UserResponse> ResetPasswordForCompanyAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    Task<UserResponse> ActivateForCompanyAsync(Guid id, CancellationToken ct = default);
    Task<UserResponse> DeactivateForCompanyAsync(Guid id, CancellationToken ct = default);
}
