namespace wa_api.Features.Campaigns;

/// <summary>
/// UAE marketing quiet-hours policy: business-initiated campaign sends are only permitted between
/// 08:00 and 20:00 UAE local time (Asia/Dubai). Used in two places:
///   1. To validate a campaign's start time up front (reject a schedule outside the window), and
///   2. To gate <c>CampaignBatchSendJob</c> at runtime — a send whose tail crosses 20:00 is deferred
///      to the next 08:00 so no message ever goes out outside the window.
///
/// UAE observes no daylight saving, so a fixed +04:00 offset is exact. Using a fixed offset also
/// avoids depending on the OS time-zone database, which differs between the Windows dev box and the
/// Linux host (IANA "Asia/Dubai" vs. a Windows id).
/// </summary>
public static class SendWindow
{
    private static readonly TimeSpan UaeOffset = TimeSpan.FromHours(4);

    /// <summary>Earliest permitted send hour, UAE local (08:00 inclusive).</summary>
    public const int OpenHour = 8;

    /// <summary>Latest permitted send hour, UAE local (20:00 inclusive — i.e. 8:00 PM sharp is allowed).</summary>
    public const int CloseHour = 20;

    /// <summary>Human-readable window description for error/notification copy.</summary>
    public const string WindowText = "8:00 AM to 8:00 PM UAE time";

    /// <summary>Convert a UTC instant to its UAE wall-clock time.</summary>
    private static DateTime ToUae(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc) + UaeOffset;

    /// <summary>True when the given UTC instant falls inside 08:00–20:00 UAE local time.</summary>
    public static bool IsOpen(DateTime utc)
    {
        var local = ToUae(utc);
        var minutes = local.Hour * 60 + local.Minute;
        return minutes >= OpenHour * 60 && minutes <= CloseHour * 60;
    }

    /// <summary>
    /// The next UTC instant at which the window is open. Returns <paramref name="utcNow"/> unchanged
    /// when the window is already open; otherwise the coming 08:00 UAE (today if it is still before
    /// 08:00 UAE, else tomorrow).
    /// </summary>
    public static DateTime NextOpen(DateTime utcNow)
    {
        if (IsOpen(utcNow)) return utcNow;

        var local = ToUae(utcNow);
        var todayOpen = local.Date.AddHours(OpenHour);
        var nextLocalOpen = local < todayOpen ? todayOpen : todayOpen.AddDays(1);
        return DateTime.SpecifyKind(nextLocalOpen - UaeOffset, DateTimeKind.Utc);
    }

    /// <summary>
    /// Best-effort check that a recurring cron expression fires inside the window. Hangfire runs
    /// recurring jobs in UTC, so the cron's hour/minute are interpreted as UTC. Returns:
    ///   <c>true</c>  — a single fixed hour:minute was parsed and lands inside the window,
    ///   <c>false</c> — a single fixed hour:minute was parsed and lands OUTSIDE the window,
    ///   <c>null</c>  — the expression uses wildcards/ranges/lists/steps and can't be validated
    ///                  statically (the runtime gate in the batch job still enforces the window).
    /// </summary>
    public static bool? CronStartsInWindow(string cron)
    {
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 5-field "m h dom mon dow" or 6-field "s m h dom mon dow" (leading seconds).
        int minIdx, hourIdx;
        if (parts.Length == 5) { minIdx = 0; hourIdx = 1; }
        else if (parts.Length == 6) { minIdx = 1; hourIdx = 2; }
        else return null;

        if (!int.TryParse(parts[minIdx], out var min) || !int.TryParse(parts[hourIdx], out var hour))
            return null; // wildcard/range/list/step — indeterminate, leave to the runtime gate
        if (min is < 0 or > 59 || hour is < 0 or > 23)
            return null;

        // A representative UTC instant at that hour:minute; only the time-of-day matters to IsOpen.
        return IsOpen(new DateTime(2001, 1, 1, hour, min, 0, DateTimeKind.Utc));
    }
}
