using Microsoft.EntityFrameworkCore;
using wa_api.Features.Campaigns.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Features.Monitoring.Dtos;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Monitoring;

/// <summary>
/// Cross-tenant health aggregation — intentionally bypasses the global CompanyId
/// query filter using IgnoreQueryFilters() so every tenant's data is visible.
/// Call sites are restricted to SuperAdmin-only endpoints.
/// </summary>
public class MonitoringService(AppDbContext db)
{
    private const double FlagThreshold = 0.10; // 10% failure rate triggers a flag

    public async Task<MonitoringResponse> GetCompanyHealthAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var companies = await db.Companies
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var sentStats = await db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.Direction == MessageDirection.Outbound && m.CreatedAt >= cutoff)
            .GroupBy(m => m.CompanyId)
            .Select(g => new
            {
                CompanyId    = g.Key,
                TotalSent    = g.Count(),
                FailedCount  = g.Count(m => m.Status == MessageStatus.Failed),
                BillableCount= g.Count(m => m.Billable == true),
            })
            .ToListAsync(ct);

        var lastActivity = await db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.Direction == MessageDirection.Outbound)
            .GroupBy(m => m.CompanyId)
            .Select(g => new { CompanyId = g.Key, LastAt = g.Max(m => m.CreatedAt) })
            .ToListAsync(ct);

        var activeCampaigns = await db.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Status == CampaignStatus.Running || c.Status == CampaignStatus.Scheduled)
            .GroupBy(c => c.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var qualityRows = await db.WabaConnections
            .IgnoreQueryFilters()
            .Where(w => w.IsActive && w.QualityRating != null)
            .Select(w => new { w.CompanyId, w.QualityRating })
            .ToListAsync(ct);

        var sentByCompany     = sentStats.ToDictionary(x => x.CompanyId);
        var lastByCompany     = lastActivity.ToDictionary(x => x.CompanyId, x => x.LastAt);
        var campaignsByCompany= activeCampaigns.ToDictionary(x => x.CompanyId, x => x.Count);
        var qualityByCompany  = qualityRows
            .GroupBy(x => x.CompanyId)
            .ToDictionary(g => g.Key, g => WorstQuality(g.Select(x => x.QualityRating!)));

        var dtos = companies.Select(c =>
        {
            sentByCompany.TryGetValue(c.Id, out var stats);
            lastByCompany.TryGetValue(c.Id, out var lastAt);
            campaignsByCompany.TryGetValue(c.Id, out var activeCampaignCount);
            qualityByCompany.TryGetValue(c.Id, out var quality);

            var totalSent   = stats?.TotalSent    ?? 0;
            var failed      = stats?.FailedCount  ?? 0;
            var billable    = stats?.BillableCount ?? 0;
            var failureRate = totalSent > 0 ? (double)failed / totalSent : 0.0;

            return new CompanyHealthDto(
                c.Id,
                c.Name,
                c.IsActive,
                c.CreatedAt,
                totalSent,
                failed,
                billable,
                Math.Round(failureRate, 4),
                failureRate >= FlagThreshold,
                activeCampaignCount,
                lastAt == default ? null : lastAt,
                quality
            );
        }).ToList();

        return new MonitoringResponse(dtos);
    }

    /// <summary>Pick the most concerning rating across a company's numbers: RED &gt; YELLOW &gt; GREEN.</summary>
    private static string? WorstQuality(IEnumerable<string> ratings)
    {
        static int Rank(string r) => r.ToUpperInvariant() switch
        {
            "RED" => 3,
            "YELLOW" => 2,
            "GREEN" => 1,
            _ => 0,
        };

        string? worst = null;
        var worstRank = -1;
        foreach (var r in ratings)
        {
            var rank = Rank(r);
            if (rank > worstRank) { worstRank = rank; worst = r; }
        }
        return worst;
    }
}
