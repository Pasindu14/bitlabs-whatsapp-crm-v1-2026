using Microsoft.EntityFrameworkCore;
using wa_api.Features.Bot.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Ai;

/// <summary>
/// The two halves of link sharing, kept in one place so the live reply, the follow-up nudge and the
/// preview all agree: what the model is shown (titles), and what the customer is sent (the URL).
/// </summary>
public static class BotLinkMessages
{
    /// <summary>Most links sent in one reply. More than that stops being an answer and becomes a dump.</summary>
    public const int MaxPerReply = 2;

    /// <summary>
    /// Enabled links in prompt order, titles only. Read through the tenant filter, so the caller must
    /// already have entered the company.
    /// </summary>
    public static async Task<List<AiLinkInfo>> LoadForPromptAsync(AppDbContext db, CancellationToken ct) =>
        await Enabled(db)
            .Select(l => new AiLinkInfo(l.Title, l.WhenToShare))
            .ToListAsync(ct);

    /// <summary>
    /// The messages to send for the links the model picked, in the order it picked them. A link
    /// disabled between the tool call and now is simply dropped.
    /// </summary>
    public static async Task<List<string>> BuildMessagesAsync(
        AppDbContext db, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return [];

        var wanted = ids.Take(MaxPerReply).ToList();
        var links = await Enabled(db).Where(l => wanted.Contains(l.Id)).ToListAsync(ct);

        return [.. wanted
            .Select(id => links.FirstOrDefault(l => l.Id == id))
            .OfType<BotLink>()
            .Select(Compose)];
    }

    /// <summary>"Here are some results 👇\nhttps://…" — the URL on its own line so WhatsApp previews it.</summary>
    public static string Compose(BotLink link)
    {
        var lead = string.IsNullOrWhiteSpace(link.Message) ? link.Title : link.Message.Trim();
        return $"{lead}\n{link.Url}";
    }

    public static IQueryable<BotLink> Enabled(AppDbContext db) =>
        db.BotLinks.AsNoTracking()
            .Where(l => l.IsActive && l.IsEnabled)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.CreatedAt);
}
