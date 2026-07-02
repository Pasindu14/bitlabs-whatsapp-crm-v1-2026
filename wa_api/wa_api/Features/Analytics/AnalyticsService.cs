using Microsoft.EntityFrameworkCore;
using wa_api.Features.Analytics.DTOs;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Analytics;

public class AnalyticsService(AppDbContext db)
{
    public async Task<MessageMetricsResponse> GetMessageMetricsAsync(
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var (start, end) = ResolveRange(fromDate, toDate);

        var rows = await db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound
                     && m.CreatedAt >= start && m.CreatedAt <= end)
            .GroupBy(m => new { Date = m.CreatedAt.Date, m.Status })
            .Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var totalSent = rows.Where(r => r.Status == MessageStatus.Sent).Sum(r => r.Count)
                      + rows.Where(r => r.Status == MessageStatus.Delivered).Sum(r => r.Count)
                      + rows.Where(r => r.Status == MessageStatus.Read).Sum(r => r.Count)
                      + rows.Where(r => r.Status == MessageStatus.Failed).Sum(r => r.Count);

        var totalDelivered = rows.Where(r => r.Status == MessageStatus.Delivered).Sum(r => r.Count)
                           + rows.Where(r => r.Status == MessageStatus.Read).Sum(r => r.Count);

        var totalRead    = rows.Where(r => r.Status == MessageStatus.Read).Sum(r => r.Count);
        var totalFailed  = rows.Where(r => r.Status == MessageStatus.Failed).Sum(r => r.Count);

        var byDate = rows.GroupBy(r => r.Date).OrderBy(g => g.Key);

        var daily = byDate.Select(g => new DailyMessageCount(
            Date:      g.Key.ToString("yyyy-MM-dd"),
            Sent:      g.Where(r => r.Status == MessageStatus.Sent).Sum(r => r.Count)
                     + g.Where(r => r.Status == MessageStatus.Delivered).Sum(r => r.Count)
                     + g.Where(r => r.Status == MessageStatus.Read).Sum(r => r.Count)
                     + g.Where(r => r.Status == MessageStatus.Failed).Sum(r => r.Count),
            Delivered: g.Where(r => r.Status == MessageStatus.Delivered).Sum(r => r.Count)
                     + g.Where(r => r.Status == MessageStatus.Read).Sum(r => r.Count),
            Read:      g.Where(r => r.Status == MessageStatus.Read).Sum(r => r.Count),
            Failed:    g.Where(r => r.Status == MessageStatus.Failed).Sum(r => r.Count)
        )).ToList();

        return new MessageMetricsResponse(totalSent, totalDelivered, totalRead, totalFailed, daily);
    }

    public async Task<CampaignPerformanceResponse> GetCampaignPerformanceAsync(
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var (start, end) = ResolveRange(fromDate, toDate);

        var campaigns = await db.Campaigns
            .Where(c => c.CreatedAt >= start && c.CreatedAt <= end
                     && c.Status != CampaignStatus.Draft)
            .OrderByDescending(c => c.LaunchedAt ?? c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Status,
                c.LaunchedAt,
                c.TotalRecipients,
                Sent      = c.Recipients.Count(r => r.Status == RecipientStatus.Sent),
                Delivered = c.Recipients.Count(r => r.Status == RecipientStatus.Delivered),
                Read      = c.Recipients.Count(r => r.Status == RecipientStatus.Read),
                Failed    = c.Recipients.Count(r => r.Status == RecipientStatus.Failed),
                Skipped   = c.Recipients.Count(r => r.Status == RecipientStatus.Skipped),
            })
            .Take(100)
            .ToListAsync(ct);

        var rows = campaigns.Select(c => new CampaignPerformanceRow(
            c.Id, c.Name, c.Status.ToString(), c.LaunchedAt, c.TotalRecipients,
            c.Sent, c.Delivered, c.Read, c.Failed, c.Skipped
        )).ToList();

        return new CampaignPerformanceResponse(rows);
    }

    public async Task<CostAnalyticsResponse> GetCostAnalyticsAsync(
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var (start, end) = ResolveRange(fromDate, toDate);

        var rows = await db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound
                     && m.CreatedAt >= start && m.CreatedAt <= end
                     && m.Billable != null)
            .GroupBy(m => new { m.Category, m.Billable })
            .Select(g => new { g.Key.Category, g.Key.Billable, Count = g.Count() })
            .ToListAsync(ct);

        var totalBillable    = rows.Where(r => r.Billable == true).Sum(r => r.Count);
        var totalNonBillable = rows.Where(r => r.Billable == false).Sum(r => r.Count);

        var breakdown = rows
            .Select(r => new BillableCategoryCount(
                r.Category ?? "unknown",
                r.Billable!.Value,
                r.Count))
            .OrderBy(r => r.Category)
            .ThenByDescending(r => r.Billable)
            .ToList();

        return new CostAnalyticsResponse(totalBillable, totalNonBillable, breakdown);
    }

    private static (DateTime from, DateTime to) ResolveRange(DateTime? from, DateTime? to)
    {
        var end   = (to   ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
        var start = (from ?? end.Date.AddDays(-29));
        if ((end - start).TotalDays > 365)
            start = end.Date.AddDays(-365);
        // Query-string dates arrive as Kind=Unspecified; the CreatedAt column is
        // timestamptz, so Npgsql rejects non-UTC parameters. Stamp them UTC.
        return (AsUtc(start), AsUtc(end));
    }

    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
