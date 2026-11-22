using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Audit;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Common;

/// <summary>
/// The audit trail has to say who made a change. Tokens carry the user id as the raw <c>sub</c> claim
/// (<c>MapInboundClaims = false</c>), and the interceptor once looked for the mapped name instead — so
/// every entry was written with nobody on it.
/// </summary>
public class AuditInterceptorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed class Tenant : ITenantContext
    {
        public Guid? CompanyId => AuditInterceptorTests.CompanyId;
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb(HttpContext? http) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"audit-{Guid.NewGuid()}")
                .AddInterceptors(new AuditInterceptor(new HttpContextAccessor { HttpContext = http }))
                .Options,
            new Tenant());

    private static Contact NewContact() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, Name = "Mona", Phone = "971501234567",
    };

    [Fact]
    public async Task AChangeMadeInARequest_RecordsTheSignedInUser()
    {
        var userId = Guid.NewGuid();

        // The claims a token from JwtTokenService carries, under the names it uses.
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString()), new Claim("role", "CompanyAdmin")], "Bearer")),
        };

        await using var db = NewDb(http);
        db.Contacts.Add(NewContact());
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == nameof(Contact));
        Assert.Equal(userId.ToString(), entry.ChangedBy);
    }

    [Fact]
    public async Task AChangeMadeOutsideARequest_HasNoUser()
    {
        await using var db = NewDb(http: null);
        db.Contacts.Add(NewContact());
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == nameof(Contact));
        Assert.Null(entry.ChangedBy);
    }
}
