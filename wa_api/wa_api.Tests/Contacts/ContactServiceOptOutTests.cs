using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts;
using wa_api.Features.Contacts.Dtos;
using wa_api.Features.Contacts.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Contacts;

/// <summary>
/// A company admin is the only sanctioned way to lift (or apply) a STOP suppression. These tests pin the
/// write semantics of <see cref="ContactService.UpdateAsync"/> and the <c>isOptedOut</c> list filter.
/// Backed by EF Core InMemory with a tenant context scoping rows to one company.
/// </summary>
public class ContactServiceOptOutTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb(Guid companyId) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"contacts-optout-{Guid.NewGuid()}")
                .Options,
            new Tenant(companyId));

    private static Contact NewContact(Guid companyId, string phone, bool optedOut = false, bool optedIn = false) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Phone = phone,
        Name = "Test " + phone,
        IsActive = true,
        IsOptedOut = optedOut,
        OptedOutAt = optedOut ? DateTime.UtcNow : null,
        HasOptedIn = optedIn,
        OptedInAt = optedIn ? DateTime.UtcNow : null,
        ConsentSource = optedIn ? ConsentSource.ManualEntry : ConsentSource.None,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task UpdateAsync_AdminOptsOut_SuppressesAndClearsConsent()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var c = NewContact(companyId, "94770000001", optedIn: true);
        db.Contacts.Add(c);
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        var result = await svc.UpdateAsync(c.Id, new UpdateContactRequest(c.Phone, c.Name, IsOptedOut: true));

        Assert.True(result.IsOptedOut);
        Assert.NotNull(result.OptedOutAt);
        Assert.False(result.HasOptedIn);                       // mirrors the webhook STOP path
        Assert.Equal(ConsentSource.None, result.ConsentSource);
    }

    [Fact]
    public async Task UpdateAsync_AdminLiftsOptOut_ReenablesSending()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var c = NewContact(companyId, "94770000002", optedOut: true);
        db.Contacts.Add(c);
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        var result = await svc.UpdateAsync(c.Id, new UpdateContactRequest(c.Phone, c.Name, IsOptedOut: false));

        Assert.False(result.IsOptedOut);
        Assert.Null(result.OptedOutAt);
    }

    [Fact]
    public async Task UpdateAsync_OptOutWins_WhenBothFlagsSetInOneRequest()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var c = NewContact(companyId, "94770000003");
        db.Contacts.Add(c);
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        var result = await svc.UpdateAsync(
            c.Id, new UpdateContactRequest(c.Phone, c.Name, HasOptedIn: true, IsOptedOut: true));

        Assert.True(result.IsOptedOut);   // suppression takes precedence over a same-request opt-in
        Assert.False(result.HasOptedIn);
    }

    [Fact]
    public async Task UpdateAsync_NullIsOptedOut_LeavesSuppressionUnchanged()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var c = NewContact(companyId, "94770000004", optedOut: true);
        db.Contacts.Add(c);
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        var result = await svc.UpdateAsync(c.Id, new UpdateContactRequest(c.Phone, "Renamed"));

        Assert.True(result.IsOptedOut);          // untouched when the flag is omitted
        Assert.Equal("Renamed", result.Name);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByOptedOut()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        db.Contacts.AddRange(
            NewContact(companyId, "94770000010", optedOut: true),
            NewContact(companyId, "94770000011", optedOut: true),
            NewContact(companyId, "94770000012", optedOut: false));
        await db.SaveChangesAsync();

        var svc = new ContactService(db);

        var suppressed = await svc.GetPagedAsync(1, 50, null, null, null, isOptedOut: true);
        Assert.Equal(2, suppressed.Total);
        Assert.All(suppressed.Items, i => Assert.True(i.IsOptedOut));

        var sendable = await svc.GetPagedAsync(1, 50, null, null, null, isOptedOut: false);
        Assert.Equal(1, sendable.Total);

        var all = await svc.GetPagedAsync(1, 50, null, null, null);
        Assert.Equal(3, all.Total);
    }
}
