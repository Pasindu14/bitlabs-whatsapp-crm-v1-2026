using wa_api.Features.Companies.Dtos;

namespace wa_api.Features.Companies;

public interface ICompanyService
{
    /// <summary>Paged, searchable list of companies (newest first by default).</summary>
    Task<(IReadOnlyList<CompanyResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? sortBy, string? sortOrder,
        CancellationToken ct = default);

    /// <summary>Loads a single company by id. Throws if not found.</summary>
    Task<CompanyResponse> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a company. Throws on duplicate name/slug.</summary>
    Task<CompanyResponse> CreateAsync(CreateCompanyRequest request, CancellationToken ct = default);

    /// <summary>Atomically creates a company and its first CompanyAdmin in one transaction.</summary>
    Task<ProvisionCompanyResponse> ProvisionAsync(ProvisionCompanyRequest request, CancellationToken ct = default);

    /// <summary>Updates all editable fields of a company. Throws on not found / duplicate name/slug.</summary>
    Task<CompanyResponse> UpdateAsync(Guid id, UpdateCompanyRequest request, CancellationToken ct = default);

    /// <summary>Marks a company active (IsActive = true). Throws if not found.</summary>
    Task<CompanyResponse> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marks a company inactive (IsActive = false). Throws if not found.</summary>
    Task<CompanyResponse> DeactivateAsync(Guid id, CancellationToken ct = default);
}
