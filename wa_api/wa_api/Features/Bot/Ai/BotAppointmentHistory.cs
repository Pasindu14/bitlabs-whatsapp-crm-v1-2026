using Microsoft.EntityFrameworkCore;
using wa_api.Features.Calendar.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Ai;

/// <summary>One of the customer's bookings, as the model sees it. <see cref="StartLocal"/> is in the company's zone.</summary>
public sealed record AiBookingInfo(string ServiceName, DateTime StartLocal, AppointmentStatus Status, bool IsUpcoming);

/// <summary>
/// What the customer actually has booked, read from the appointments table — the single source of truth
/// for "are they booked?". The reply, the follow-up nudge and <c>find_appointments</c> all use it, so the
/// assistant can never say "you're all set" in one message and "you have nothing booked" in the next.
/// <para>
/// It replaces reading <c>Conversation.BotConvertedAt</c> as "booked": that stamp is set when the bot books
/// and never cleared, so once the appointment had passed (or been cancelled) the customer was still told
/// they were booked.
/// </para>
/// </summary>
public static class BotAppointmentHistory
{
    /// <summary>Most past or cancelled bookings shown. Enough for "when was my last visit", not a full history.</summary>
    public const int MaxRecent = 3;

    /// <summary>How far back past bookings are shown. Older than this is not "recent" to anyone.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(365);

    /// <summary>
    /// Upcoming (not cancelled, not yet started) soonest first, then the most recent past or cancelled
    /// ones, newest first. Read through the tenant filter, so the caller must already have entered the
    /// company.
    /// </summary>
    public static async Task<List<AiBookingInfo>> LoadAsync(
        AppDbContext db, Guid contactId, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var since = now - RecentWindow;

        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.ContactId == contactId && a.StartUtc >= since)
            .Select(a => new { a.AppointmentType.Name, a.StartUtc, a.Status })
            .ToListAsync(ct);

        static bool IsUpcoming(AppointmentStatus status, DateTime startUtc, DateTime now) =>
            startUtc >= now && status is AppointmentStatus.Confirmed or AppointmentStatus.Pending;

        var upcoming = rows
            .Where(r => IsUpcoming(r.Status, r.StartUtc, now))
            .OrderBy(r => r.StartUtc);

        var recent = rows
            .Where(r => !IsUpcoming(r.Status, r.StartUtc, now))
            .OrderByDescending(r => r.StartUtc)
            .Take(MaxRecent);

        return
        [
            .. upcoming.Select(r => new AiBookingInfo(
                r.Name, TimeZoneInfo.ConvertTimeFromUtc(r.StartUtc, timeZone), r.Status, IsUpcoming: true)),
            .. recent.Select(r => new AiBookingInfo(
                r.Name, TimeZoneInfo.ConvertTimeFromUtc(r.StartUtc, timeZone), r.Status, IsUpcoming: false)),
        ];
    }

    /// <summary>How a past or cancelled booking reads to the model: what actually happened to it.</summary>
    public static string Outcome(AiBookingInfo b) => b.Status switch
    {
        AppointmentStatus.Cancelled => "cancelled",
        AppointmentStatus.NoShow => "did not come",
        AppointmentStatus.Completed => "attended",
        // Confirmed or a lapsed hold that is now in the past: it was booked, and the date has gone by.
        _ => "past",
    };
}
