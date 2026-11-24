using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using wa_api.Common.Subscriptions;
using wa_api.Features.Auth;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot;

/// <summary>Platform-level chatbot settings (<c>Bot</c> section).</summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>
    /// Master switch for the whole chatbot, every tenant. Off means the webhook hands nothing to the bot and
    /// the jobs stand down — the CRM behaves exactly as it did before the chatbot was merged in. Ships off.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long the "is the bot live for this company" answer is remembered. The inbound webhook asks on
    /// every message, so a short cache keeps a campaign reply surge from turning into a query per message.
    /// The cost is that switching the bot on or off takes up to this long to be noticed.
    /// </summary>
    public int GateCacheSeconds { get; set; } = 30;

    /// <summary>
    /// How far back the bot looks for campaign messages sent to the customer it is answering. A reply to a
    /// campaign is only understandable with the campaign in view.
    /// </summary>
    public int CampaignContextDays { get; set; } = 7;

    /// <summary>
    /// A message older than this when its reply job finally runs is left for a person. Queue backlog after a
    /// campaign can delay a job; answering forty minutes late reads as the business messaging out of nowhere.
    /// </summary>
    public int MaxReplyAgeMinutes { get; set; } = 15;
}

/// <summary>
/// The single place that decides whether the chatbot may act for a company. Three things must all be true:
/// the platform switch is on, the company's plan includes the chatbot, and the company has actually set
/// something up (an enabled AI profile or at least one enabled keyword rule).
/// </summary>
public interface IBotGate
{
    /// <summary>The platform master switch (<see cref="BotOptions.Enabled"/>).</summary>
    bool GloballyEnabled { get; }

    BotOptions Options { get; }

    /// <summary>The company's plan carries <c>manage_bot</c>.</summary>
    Task<bool> IsEntitledAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Switch on, plan entitled, and something configured. Cached briefly per company.</summary>
    Task<bool> IsLiveAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>The company has an active, enabled clinic CRM connection. Cached briefly per company.</summary>
    Task<bool> HasClinicCrmAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// The company's enabled clinic CRM connection also wants to hear about inbound messages. Cached briefly.
    /// </summary>
    Task<bool> NotifiesClinicCrmOfMessagesAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Drops the cached answers for a company — called when its bot settings change.</summary>
    void Forget(Guid companyId);
}

public sealed class BotGate(
    AppDbContext db,
    IPlanFeatureGate planFeatures,
    IMemoryCache cache,
    IOptions<BotOptions> options) : IBotGate
{
    public bool GloballyEnabled => options.Value.Enabled;

    public BotOptions Options => options.Value;

    private TimeSpan CacheFor => TimeSpan.FromSeconds(Math.Max(0, options.Value.GateCacheSeconds));

    public Task<bool> IsEntitledAsync(Guid companyId, CancellationToken ct = default) =>
        planFeatures.HasFeatureAsync(companyId, Permission.ManageBot, ct);

    public async Task<bool> IsLiveAsync(Guid companyId, CancellationToken ct = default)
    {
        // The master switch is read live, never cached: flipping it off must stop the bot at once.
        if (!GloballyEnabled) return false;

        return await CachedAsync(LiveKey(companyId), async () =>
        {
            if (!await IsEntitledAsync(companyId, ct)) return false;

            // IgnoreQueryFilters + explicit company: called from the webhook job, which has no tenant.
            if (await db.BotProfiles.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(p => p.CompanyId == companyId && p.IsActive && p.IsEnabled, ct))
                return true;

            return await db.AutoReplyRules.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(r => r.CompanyId == companyId && r.IsActive && r.IsEnabled, ct);
        });
    }

    public Task<bool> HasClinicCrmAsync(Guid companyId, CancellationToken ct = default) =>
        CachedAsync(ClinicKey(companyId), () =>
            db.ClinicCrmConnections.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(c => c.CompanyId == companyId && c.IsActive && c.IsEnabled, ct));

    public Task<bool> NotifiesClinicCrmOfMessagesAsync(Guid companyId, CancellationToken ct = default) =>
        CachedAsync(ClinicNotifyKey(companyId), () =>
            db.ClinicCrmConnections.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(c => c.CompanyId == companyId && c.IsActive && c.IsEnabled && c.NotifyOnMessages, ct));

    public void Forget(Guid companyId)
    {
        cache.Remove(LiveKey(companyId));
        cache.Remove(ClinicKey(companyId));
        cache.Remove(ClinicNotifyKey(companyId));
    }

    private async Task<bool> CachedAsync(string key, Func<Task<bool>> load)
    {
        if (cache.TryGetValue(key, out bool known)) return known;

        var value = await load();
        if (CacheFor > TimeSpan.Zero) cache.Set(key, value, CacheFor);
        return value;
    }

    private static string LiveKey(Guid companyId) => $"bot-gate:live:{companyId}";
    private static string ClinicKey(Guid companyId) => $"bot-gate:clinic:{companyId}";
    private static string ClinicNotifyKey(Guid companyId) => $"bot-gate:clinic-notify:{companyId}";
}
