using System.Security.Claims;
using wa_api.Features.Auth;

namespace wa_api.Common.Tenancy;

/// <summary>
/// Resolves the tenant from the authenticated principal's JWT claims. Reads the RAW claim
/// names emitted by <c>JwtTokenService</c> ("role", "companyId") — matching the bearer setup
/// in Program.cs (<c>MapInboundClaims = false</c>).
/// <para>
/// Registered scoped, so one instance reflects exactly one request. When there is no HTTP
/// context (migrations, seeding, background jobs) it falls back to <see cref="NullTenantContext"/>
/// semantics: no company, not super-admin.
/// </para>
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        IsSuperAdmin = string.Equals(
            user.FindFirstValue("role"), nameof(UserRole.SuperAdmin), StringComparison.Ordinal);

        if (Guid.TryParse(user.FindFirstValue("companyId"), out var companyId))
            CompanyId = companyId;
    }

    public Guid? CompanyId { get; }

    public bool IsSuperAdmin { get; }
}

/// <summary>
/// Null-object tenant: no company, not super-admin. Used at design time (EF CLI) and any other
/// place a real per-request tenant is unavailable. With no company and no super-admin bypass,
/// tenant-scoped queries naturally return nothing — fail closed, never leak.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();
    private NullTenantContext() { }
    public Guid? CompanyId => null;
    public bool IsSuperAdmin => false;
}
