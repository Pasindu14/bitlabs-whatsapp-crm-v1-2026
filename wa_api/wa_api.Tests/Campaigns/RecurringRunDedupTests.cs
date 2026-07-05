using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Campaigns;

/// <summary>
/// Pins the run-scoped duplicate guard behind the recurring-campaign fix. A Recurring campaign re-sends
/// to the same contacts on every firing; CampaignBatchSendJob's "already messaged this contact?" guard is
/// therefore scoped to the CURRENT run (<c>CampaignRunNumber == campaign.CurrentRunNumber</c>). Before the
/// fix the guard matched any message for the campaign regardless of run, so run 2 saw run 1's message and
/// skipped every contact — the campaign silently sent nothing after the first run. These tests mirror that
/// exact query and fail if the run scoping is dropped.
/// </summary>
public class RecurringRunDedupTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    private static AppDbContext NewDb(Guid companyId) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"recurring-dedup-{Guid.NewGuid()}")
                .Options,
            new Tenant(companyId));

    private static Message CampaignMessage(Guid companyId, Guid campaignId, Guid contactId, int? run) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ContactId = contactId,
        WabaConnectionId = Guid.NewGuid(),
        CampaignId = campaignId,
        CampaignRunNumber = run,
        Direction = MessageDirection.Outbound,
        Status = MessageStatus.Sent,
        ExternalMessageId = "wamid." + Guid.NewGuid(),
        Body = "x",
    };

    // Mirrors CampaignBatchSendJob's alreadyMessaged query.
    private static async Task<HashSet<Guid>> AlreadyMessagedAsync(
        AppDbContext db, Guid campaignId, int currentRun, IReadOnlyCollection<Guid> batchContactIds) =>
        new(await db.Messages.IgnoreQueryFilters()
            .Where(m => m.CampaignId == campaignId
                        && m.CampaignRunNumber == currentRun
                        && batchContactIds.Contains(m.ContactId))
            .Select(m => m.ContactId)
            .ToListAsync());

    [Fact]
    public async Task Run2_DoesNotTreatRun1Message_AsAlreadySent()
    {
        var company = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var contact = Guid.NewGuid();

        await using var db = NewDb(company);
        db.Messages.Add(CampaignMessage(company, campaign, contact, run: 1));
        await db.SaveChangesAsync();

        // The campaign is now on run 2; the contact must be eligible to receive run 2's message.
        var alreadyMessaged = await AlreadyMessagedAsync(db, campaign, currentRun: 2, new[] { contact });

        Assert.DoesNotContain(contact, alreadyMessaged);
    }

    [Fact]
    public async Task WithinTheSameRun_MessageStillSuppressesResend()
    {
        // Retry-safety: the guard must still catch a duplicate WITHIN the current run (the crash window where
        // Meta accepted a send but the recipient status flip wasn't committed before the job was retried).
        var company = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var contact = Guid.NewGuid();

        await using var db = NewDb(company);
        db.Messages.Add(CampaignMessage(company, campaign, contact, run: 3));
        await db.SaveChangesAsync();

        var alreadyMessaged = await AlreadyMessagedAsync(db, campaign, currentRun: 3, new[] { contact });

        Assert.Contains(contact, alreadyMessaged);
    }

    [Fact]
    public async Task OnlyContactsMessagedInThisRun_AreSuppressed()
    {
        var company = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var sentThisRun = Guid.NewGuid();
        var sentLastRun = Guid.NewGuid();
        var neverSent = Guid.NewGuid();

        await using var db = NewDb(company);
        db.Messages.Add(CampaignMessage(company, campaign, sentThisRun, run: 2));
        db.Messages.Add(CampaignMessage(company, campaign, sentLastRun, run: 1));
        await db.SaveChangesAsync();

        var batch = new[] { sentThisRun, sentLastRun, neverSent };
        var alreadyMessaged = await AlreadyMessagedAsync(db, campaign, currentRun: 2, batch);

        Assert.Contains(sentThisRun, alreadyMessaged);       // sent this run → skip
        Assert.DoesNotContain(sentLastRun, alreadyMessaged); // sent a prior run → re-send this run
        Assert.DoesNotContain(neverSent, alreadyMessaged);   // never sent → send
    }
}
