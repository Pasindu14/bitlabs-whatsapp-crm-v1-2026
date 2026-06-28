using wa_api.Features.Packages.Dtos;

namespace wa_api.Features.Packages;

/// <summary>SuperAdmin management of the message-credit package catalog.</summary>
public interface IPackageService
{
    Task<(IReadOnlyList<PackageResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    Task<PackageResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PackageResponse> CreateAsync(CreatePackageRequest request, CancellationToken ct = default);

    Task<PackageResponse> UpdateAsync(Guid id, UpdatePackageRequest request, CancellationToken ct = default);

    Task<PackageResponse> ActivateAsync(Guid id, CancellationToken ct = default);

    Task<PackageResponse> DeactivateAsync(Guid id, CancellationToken ct = default);
}
