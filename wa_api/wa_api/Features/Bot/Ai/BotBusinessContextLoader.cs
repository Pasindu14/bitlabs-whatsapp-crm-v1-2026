using Microsoft.EntityFrameworkCore;
using wa_api.Features.Bot.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Loads everything the assistant knows about the business: the profile, bookable services, prepared
/// answers, opening hours and links. The one place this is built, so a live reply, a follow-up and the
/// settings preview always see exactly the same facts.
/// </summary>
public static class BotBusinessContextLoader
{
    public static async Task<AiBusinessContext> LoadAsync(
        AppDbContext db, BotProfile profile, IClinicCrmBookingService? clinicCrm, CancellationToken ct)
    {
        var services = await db.AppointmentTypes.AsNoTracking()
            .Where(t => t.IsActive && t.IsEnabled)
            .OrderBy(t => t.Name)
            .Select(t => new AiServiceInfo(t.Id, t.Name, t.Description, t.DurationMinutes, t.Price))
            .ToListAsync(ct);

        var faqs = await db.BotFaqs.AsNoTracking()
            .Where(f => f.IsActive && f.IsEnabled)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt)
            .Select(f => new AiFaqInfo(f.Question, f.Answer))
            .ToListAsync(ct);

        var hours = await db.BusinessHours.AsNoTracking()
            .Where(h => h.IsActive)
            .Select(h => new { h.DayOfWeek, h.StartLocal, h.EndLocal })
            .ToListAsync(ct);

        var openingHours = hours
            .GroupBy(h => h.DayOfWeek)
            .OrderBy(g => ((int)g.Key + 6) % 7)          // Monday first, the way an opening-hours sign reads
            .Select(g => new AiOpeningHoursLine(
                g.Key.ToString(),
                string.Join(", ", g.OrderBy(h => h.StartLocal)
                    .Select(h => $"{h.StartLocal:HH\\:mm} - {h.EndLocal:HH\\:mm}"))))
            .ToList();

        // With the clinic CRM connected, the clinic's calendar decides when it is open. Our own business
        // hours would contradict it (e.g. "Friday: open" for a clinic closed on Fridays), so the model gets
        // the clinic's hours from check_availability instead.
        if (clinicCrm is not null && await clinicCrm.GetEndpointAsync(ct) is not null)
            openingHours = [];

        return new AiBusinessContext(
            profile.BusinessName,
            profile.BusinessDescription,
            profile.ToneNotes,
            profile.Location,
            profile.ContactNote,
            profile.BookingInstructions,
            profile.PreferredLanguage,
            profile.CurrencyLabel,
            services,
            faqs,
            openingHours,
            await BotLinkMessages.LoadForPromptAsync(db, ct),
            profile.IndustryRules,
            profile.BookingsEnabled);
    }
}
