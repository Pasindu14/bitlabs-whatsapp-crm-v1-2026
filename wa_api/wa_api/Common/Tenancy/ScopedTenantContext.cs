using System.Security.Claims;
using wa_api.Features.Auth;

namespace wa_api.Common.Tenancy;

/// <summary>
/// The tenant for one DI scope. On the HTTP path that is the authenticated principal's company, read
/// from the RAW claim names emitted by <c>JwtTokenService</c> ("role", "companyId") — matching the
/// bearer setup in Program.cs (<c>MapInboundClaims = false</c>).
/// <para>
/// A background job has no principal, so it starts with no company and tenant-scoped queries return
/// nothing — fail closed. A job that legitimately acts for one tenant calls
/// <see cref="EnterCompany"/> once at the top of its scope, and from then on the global query filter,
/// the <c>CompanyId</c> auto-stamp, and every tenant-scoped service behave exactly as they do in a
/// request. That is what lets the reply job reuse <c>IAvailabilityService</c> and
/// <c>IAppointmentService</c> rather than reimplementing the offerability and overlap rules — the two
/// pieces of logic in this codebase that must never have a second, subtly different copy.
/// </para>
/// <para>
/// Registered scoped, so one instance reflects exactly one request or one job.
/// </para>
/// </summary>
public sealed class ScopedTenantContext : ITenantContext
{
    /// <summary>
    /// True when an authenticated principal seeded this scope. Latching it is what makes
    /// <see cref="EnterCompany"/> safe: inside a request the tenant is decided by the JWT and nothing
    /// downstream may move it, so the method that exists for jobs cannot become a way to cross tenants
    /// mid-request.
    /// </summary>
    private readonly bool _seededFromPrincipal;

    public ScopedTenantContext(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        _seededFromPrincipal = true;

        IsSuperAdmin = string.Equals(
            user.FindFirstValue("role"), nameof(UserRole.SuperAdmin), StringComparison.Ordinal);

        if (Guid.TryParse(user.FindFirstValue("companyId"), out var companyId))
            CompanyId = companyId;
    }

    public Guid? CompanyId { get; private set; }

    public bool IsSuperAdmin { get; private set; }

    /// <summary>
    /// Binds this scope to <paramref name="companyId"/>. For background jobs only, and only before the
    /// scope's first tenant-scoped query.
    /// <para>
    /// The company must be derived from data the job already owns — the WABA connection the webhook
    /// arrived on, or the message being answered — never from anything a customer sent.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The scope belongs to an authenticated request, or a different company was already entered.
    /// Both are bugs that would cross a tenant boundary, so they fail loudly rather than silently.
    /// </exception>
    public void EnterCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("A tenant scope needs a real company id.", nameof(companyId));

        if (_seededFromPrincipal)
            throw new InvalidOperationException(
                "The tenant for a request scope comes from the JWT and cannot be reassigned.");

        if (CompanyId is { } existing && existing != companyId)
            throw new InvalidOperationException(
                "This scope is already acting for a different company. Use one scope per tenant.");

        CompanyId = companyId;
    }
}

/// <summary>
/// Null-object tenant: no company, not super-admin. Used at design time (EF CLI) and any other
/// place a real per-scope tenant is unavailable. With no company and no super-admin bypass,
/// tenant-scoped queries naturally return nothing — fail closed, never leak.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();
    private NullTenantContext() { }
    public Guid? CompanyId => null;
    public bool IsSuperAdmin => false;
}
