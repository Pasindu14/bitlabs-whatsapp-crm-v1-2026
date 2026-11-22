using System.Globalization;

namespace wa_api.Features.ClinicCrm;

/// <summary>
/// The CRM's wire formats: <c>yyyy-MM-dd</c> dates, 24-hour <c>HH:mm</c> times, numbers with a country code.
/// </summary>
public static class ClinicCrmFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Invariant);

    public static string Time(TimeOnly time) => time.ToString("HH:mm", Invariant);

    /// <summary>
    /// <c>+</c> and the digits. Our contacts are stored as bare digits with the country code
    /// (<c>971555520787</c>) because that is what Meta sends; the CRM wants the <c>+</c>.
    /// </summary>
    public static string WhatsApp(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        return "+" + digits;
    }

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out date);

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, ["HH:mm", "H:mm", "HH:mm:ss"], Invariant, DateTimeStyles.None, out time);

    /// <summary>A CRM date and time, read in the CRM's zone, as a UTC instant. Null if either part is unreadable
    /// or the local time does not exist (a clock-change gap).</summary>
    public static DateTime? ToUtc(string? date, string? time, TimeZoneInfo crmZone)
    {
        if (!TryParseDate(date, out var d) || !TryParseTime(time, out var t)) return null;

        var local = d.ToDateTime(t, DateTimeKind.Unspecified);
        if (crmZone.IsInvalidTime(local)) return null;

        return TimeZoneInfo.ConvertTimeToUtc(local, crmZone);
    }

    /// <summary>A UTC instant as the CRM's local date and time.</summary>
    public static (string Date, string Time) FromUtc(DateTime utc, TimeZoneInfo crmZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), crmZone);
        return (Date(DateOnly.FromDateTime(local)), Time(TimeOnly.FromDateTime(local)));
    }
}
