using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts;
using wa_api.Features.Contacts.Dtos;
using wa_api.Features.Contacts.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Contacts;

/// <summary>
/// Pins bulk-import behavior: E.164 normalization, in-file + against-existing de-duplication, invalid
/// rejection, and the accuracy of the returned summary. Backed by EF Core InMemory.
/// </summary>
public class ContactImportTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb(Guid companyId) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"contacts-import-{Guid.NewGuid()}")
                .Options,
            new Tenant(companyId));

    private static ImportContactRow Row(string phone, string? name = null) => new(phone, name);

    [Fact]
    public async Task ImportAsync_NormalizesDedupesAndReportsSummary()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        // An existing contact that one import row will collide with.
        db.Contacts.Add(new Contact { Id = Guid.NewGuid(), CompanyId = companyId, Phone = "971501111111", Name = "Existing" });
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        var result = await svc.ImportAsync(new ImportContactsRequest(
        [
            Row("+971 50 222 2222", "Alice"),   // valid, new → imported (normalized)
            Row("971502222222"),                // duplicate of Alice within the file
            Row("971501111111"),                // already exists
            Row("123"),                         // invalid (too short)
            Row("971503333333", "Bob"),         // valid, new → imported
        ]));

        Assert.Equal(5, result.Submitted);
        Assert.Equal(2, result.Imported);           // Alice + Bob
        Assert.Equal(1, result.DuplicateInFile);    // second 2222
        Assert.Equal(1, result.DuplicateExisting);  // 1111
        Assert.Equal(1, result.Invalid);            // "123"
        Assert.Equal(3, result.Skipped.Count);

        // The normalized number was stored (spaces/plus stripped).
        var stored = await db.Contacts.IgnoreQueryFilters().Select(c => c.Phone).ToListAsync();
        Assert.Contains("971502222222", stored);
        Assert.Contains("971503333333", stored);
    }

    [Fact]
    public async Task ImportAsync_MarkOptedIn_StampsConsent()
    {
        var companyId = Guid.NewGuid();
        await using var db = NewDb(companyId);
        var svc = new ContactService(db);

        await svc.ImportAsync(new ImportContactsRequest([Row("971504444444", "Carol")], MarkOptedIn: true));

        var contact = await db.Contacts.IgnoreQueryFilters().SingleAsync(c => c.Phone == "971504444444");
        Assert.True(contact.HasOptedIn);
        Assert.Equal(ConsentSource.Import, contact.ConsentSource);
        Assert.NotNull(contact.OptedInAt);
    }
}
