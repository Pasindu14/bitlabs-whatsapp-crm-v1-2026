using Microsoft.EntityFrameworkCore;
using wa_api.Common.Tenancy;
using wa_api.Features.Campaigns.Entities;
using wa_api.Infrastructure.Persistence;
using Xunit;

namespace wa_api.Tests.Campaigns;

/// <summary>
/// Pins the "Skipped" vs "QuotaExceeded" split used by the campaign stats and the recipients filter. Quota-held
/// rows are stored as Skipped + QUOTA_EXCEEDED, so the two buckets must partition the Skipped rows exactly —
/// in particular a compliance skip whose ErrorCode is NULL must still count as Skipped (the "!= code" predicate
/// must not drop NULLs), or the stat cards stop summing to the total. The resend action re-queues exactly the
/// QuotaExceeded bucket, so a leak in either direction would resend an opted-out contact or strand a held one.
/// </summary>
public class QuotaHoldPartitionTests
{
    private sealed class Tenant(Guid companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool IsSuperAdmin => false;
    }

    private static CampaignRecipient Row(Guid company, Guid campaign, RecipientStatus status, string? code) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = company,
        CampaignId = campaign,
        ContactId = Guid.NewGuid(),
        Status = status,
        ErrorCode = code,
        IdempotencyKey = Guid.NewGuid().ToString(),
    };

    [Fact]
    public async Task SkippedAndQuotaExceeded_PartitionTheSkippedRows()
    {
        var company = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"quota-hold-{Guid.NewGuid()}").Options,
            new Tenant(company));

        db.CampaignRecipients.AddRange(
            Row(company, campaign, RecipientStatus.Skipped, "OPT_OUT"),
            Row(company, campaign, RecipientStatus.Skipped, null),
            Row(company, campaign, RecipientStatus.Skipped, CampaignRecipient.QuotaExceededErrorCode),
            Row(company, campaign, RecipientStatus.Skipped, CampaignRecipient.QuotaExceededErrorCode),
            Row(company, campaign, RecipientStatus.Failed, "131049"),
            Row(company, campaign, RecipientStatus.Delivered, null));
        await db.SaveChangesAsync();

        var rows = db.CampaignRecipients.IgnoreQueryFilters().Where(r => r.CampaignId == campaign);

        // Same predicates as CampaignService.GetStatsAsync / GetRecipientsAsync / ResendQuotaBlockedAsync.
        var skipped = await rows.CountAsync(r => r.Status == RecipientStatus.Skipped
                                                 && r.ErrorCode != CampaignRecipient.QuotaExceededErrorCode);
        var quotaHeld = await rows.CountAsync(r => r.Status == RecipientStatus.Skipped
                                                   && r.ErrorCode == CampaignRecipient.QuotaExceededErrorCode);

        Assert.Equal(2, skipped);   // OPT_OUT + the NULL-code skip
        Assert.Equal(2, quotaHeld); // only the quota-held rows — never the Meta failure or the delivered one
        Assert.Equal(4, skipped + quotaHeld);
    }
}
