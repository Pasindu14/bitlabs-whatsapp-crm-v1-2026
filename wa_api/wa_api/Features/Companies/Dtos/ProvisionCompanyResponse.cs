using wa_api.Features.Users.Dtos;

namespace wa_api.Features.Companies.Dtos;

/// <summary>Response from <c>POST /api/v1/companies/provision</c>.</summary>
public record ProvisionCompanyResponse(
    CompanyResponse Company,
    UserResponse Admin
);
