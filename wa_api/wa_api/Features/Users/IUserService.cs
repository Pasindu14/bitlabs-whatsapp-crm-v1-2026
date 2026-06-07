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
}
