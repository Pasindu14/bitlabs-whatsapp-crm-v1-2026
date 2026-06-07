using wa_api.Features.Companies.Dtos;

namespace wa_api.Features.Companies;

public interface ICompanyService
{
    /// <summary>Paged, searchable list of companies (newest first by default).</summary>
    Task<(IReadOnlyList<CompanyResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>Creates a company. Throws on duplicate name/slug.</summary>
    Task<CompanyResponse> CreateAsync(CreateCompanyRequest request, CancellationToken ct = default);

    /// <summary>Soft-deletes a company (IsActive = false). Throws if not found.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
