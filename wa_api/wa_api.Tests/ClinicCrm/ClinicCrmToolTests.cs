using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Features.Bot.Ai;
using wa_api.Features.Bot.Ai.Tools;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.Conversations.Entities;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>
/// The bot's tools when the company is connected to its clinic's CRM (plan 014). The rule every test here
/// circles: the customer is told "booked" only after the CRM said yes.
/// </summary>
public class ClinicCrmToolTests
{
    private static BotToolExecutor Executor(ClinicCrmTestKit kit, FakeClinicCrmBookings crm, bool dryRun = false) =>
        new(kit.Db, null!, kit.Appointments(crm),
            new BotToolScope(kit.CompanyId, kit.Contact.Id, kit.Conversation.Id, ClinicCrmTestKit.Dubai, dryRun),
            NullLogger<BotToolExecutor>.Instance,
            crm);

    private static BotToolExecutor ExecutorHearing(ClinicCrmTestKit kit, FakeClinicCrmBookings crm, string customerText) =>
        new(kit.Db, null!, kit.Appointments(crm),
            new BotToolScope(
                kit.CompanyId, kit.Contact.Id, kit.Conversation.Id, ClinicCrmTestKit.Dubai, DryRun: false,
                CustomerText: customerText),
            NullLogger<BotToolExecutor>.Instance,
            crm);

    private static Dictionary<string, System.Text.Json.JsonElement> BookArgs(DateOnly day, string time) =>
        ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("start_local", $"{day:yyyy-MM-dd}T{time}"),
            ("customer_name", "Sarah"));

    // ── the customer's own day ────────────────────────────────────────────────

    [Fact]
    public async Task ABookingOnAnotherDayThanTheCustomerJustNamed_IsRefused_EvenWhenTheModelAgreesWithItself()
    {
        // Observed live: "thursday 2pm" was booked as Monday 2 Nov, with weekday set to Monday.
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);
        var day = ClinicCrmTestKit.DaysAhead(3);
        var customerDay = day.AddDays(2).DayOfWeek.ToString().ToLowerInvariant();

        var args = BookArgs(day, "14:00");
        args["weekday"] = System.Text.Json.JsonSerializer.SerializeToElement(day.DayOfWeek.ToString());

        var result = await ExecutorHearing(kit, crm, $"{customerDay} 2pm")
            .ExecuteAsync(BotTools.BookAppointment, args, default);

        Assert.False(result.Booked);
        Assert.Contains("asked for a different day", result.Content);
        Assert.Empty(crm.Books);
    }

    [Fact]
    public async Task ASingleDayLookupOnAnotherDayThanTheCustomerNamed_IsRefused()
    {
        // Observed live: "Saturday 5pm" was looked up for the Saturday a week later.
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);
        var day = ClinicCrmTestKit.DaysAhead(3);
        var customerDay = day.AddDays(1).DayOfWeek.ToString();

        var result = await ExecutorHearing(kit, crm, $"{customerDay} 5pm can you book")
            .ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
                ("service_name", "PRP Consultation"),
                ("from_date", $"{day:yyyy-MM-dd}"),
                ("to_date", $"{day:yyyy-MM-dd}"),
                ("time", "17:00")), default);

        Assert.Contains("asked for a different day", result.Content);
    }

    [Fact]
    public async Task AMessageThatNamesNoDay_DoesNotBlockABooking()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(881, day, "14:00", "14:45")),
        };

        var result = await ExecutorHearing(kit, crm, "yes please, my name is Sarah")
            .ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "14:00"), default);

        Assert.True(result.Booked);
    }

    // ── book_appointment ──────────────────────────────────────────────────────

    [Fact]
    public async Task ADateThatIsNotTheAgreedWeekday_IsRefused_AndNothingIsBooked()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);
        var day = ClinicCrmTestKit.DaysAhead(3);
        var claimed = day.AddDays(1).DayOfWeek.ToString();

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("start_local", $"{day:yyyy-MM-dd}T16:00"),
            ("weekday", claimed),
            ("customer_name", "Sarah")), default);

        Assert.False(result.Booked);
        Assert.Contains($"is a {day.DayOfWeek}, not a {claimed}", result.Content);
        Assert.Empty(crm.Books);
    }

    [Fact]
    public async Task TheAgreedWeekday_MatchingTheDate_Books()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(880, day, "16:00", "16:45")),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("start_local", $"{day:yyyy-MM-dd}T16:00"),
            ("weekday", day.DayOfWeek.ToString().ToLowerInvariant()),
            ("customer_name", "Sarah")), default);

        Assert.True(result.Booked);
        Assert.Single(crm.Books);
    }

    [Fact]
    public async Task Accepted_IsMirroredAsSynced_WithTheClinicsBookingIdAndEndTime()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(873, day, "16:00", "16:45")),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.True(result.Booked);
        Assert.StartsWith("Booked.", result.Content);

        var draft = Assert.Single(crm.Books);
        Assert.Equal(ClinicCrmTestKit.TreatmentId, draft.TreatmentId);
        Assert.Equal(ClinicCrmTestKit.DubaiToUtc(day, 16), draft.StartUtc);
        Assert.Equal("Sarah", draft.CustomerName);   // the name they gave for this booking, not the profile name

        var mirror = await kit.Db.Appointments.SingleAsync();
        Assert.Equal(ExternalSyncStatus.Synced, mirror.ExternalSyncStatus);
        Assert.Equal(873, mirror.ExternalBookingId);
        Assert.Equal(1052, mirror.ExternalCustomerId);
        Assert.Equal(AppointmentSource.Bot, mirror.Source);
        Assert.Equal(ClinicCrmTestKit.DubaiToUtc(day, 16, 45), mirror.EndUtc);   // the clinic's 45 min, not our 30
        Assert.Equal(kit.CompanyId, mirror.CompanyId);
    }

    [Fact]
    public async Task TheChatSummary_GoesOnTheBooking_AndOurCalendar()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            Summary = "Concern: hair fall for 5 months.\nRecommended: 3-session PRP course.",
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(873, day, "16:00", "16:45")),
        };
        var args = BookArgs(day, "16:00");
        args["notes"] = System.Text.Json.JsonSerializer.SerializeToElement("Prefers mornings");

        await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, args, default);

        var draft = Assert.Single(crm.Books);
        Assert.Equal("Concern: hair fall for 5 months.\nRecommended: 3-session PRP course.\nNote: Prefers mornings", draft.Notes);

        Assert.Equal(draft.Notes, (await kit.Db.Appointments.SingleAsync()).Notes);
    }

    [Fact]
    public async Task WithoutASummary_TheBookingStillGoesThrough_WithTheBotsOwnNote()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            Summary = null,
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(873, day, "16:00", "16:45")),
        };
        var args = BookArgs(day, "16:00");
        args["notes"] = System.Text.Json.JsonSerializer.SerializeToElement("Prefers mornings");

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, args, default);

        Assert.True(result.Booked);
        var draft = Assert.Single(crm.Books);
        Assert.Equal("Prefers mornings", draft.Notes);
    }

    [Fact]
    public async Task Taken_IsNotBooked_AndOffersTheClinicsOtherTimesThatDay()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Taken, FreeTimes: ["15:00", "16:45"]),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.False(result.Booked);
        Assert.Contains("Not booked", result.Content);
        Assert.Contains($"start_local={day:yyyy-MM-dd}T15:00", result.Content);
        Assert.Contains($"start_local={day:yyyy-MM-dd}T16:45", result.Content);
        Assert.False(await kit.Db.Appointments.AnyAsync());
    }

    [Fact]
    public async Task Rejected_PassesTheClinicsReasonOn()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Rejected, Message: "The clinic is closed on Fridays."),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.False(result.Booked);
        Assert.Contains("The clinic is closed on Fridays.", result.Content);
        Assert.False(await kit.Db.Appointments.AnyAsync());
    }

    [Fact]
    public async Task ClinicUnreachable_HoldsItAsPending_QueuesThePush_AndNeverSaysBooked()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Unavailable, Message: "The clinic's system could not be reached."),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.True(result.Booked);                        // the request-received message still goes out
        Assert.Contains("NOT confirmed", result.Content);
        Assert.Contains("Do NOT say it is booked", result.Content);

        var pending = await kit.Db.Appointments.SingleAsync();
        Assert.Equal(ExternalSyncStatus.Pending, pending.ExternalSyncStatus);
        Assert.Null(pending.ExternalBookingId);
        Assert.Equal(pending.Id, Assert.Single(crm.Queued));
    }

    [Fact]
    public async Task AServiceNotMappedToATreatment_StillBooks_WithNoTreatment()
    {
        var kit = await ClinicCrmTestKit.CreateAsync(mapped: false);
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(873, day, "16:00", "16:45")),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.True(result.Booked);
        Assert.Null(Assert.Single(crm.Books).TreatmentId);
    }

    [Fact]
    public async Task Preview_DoesNotTouchTheClinic()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);

        var result = await Executor(kit, crm, dryRun: true).ExecuteAsync(
            BotTools.BookAppointment, BookArgs(ClinicCrmTestKit.DaysAhead(3), "16:00"), default);

        Assert.StartsWith("[preview]", result.Content);
        Assert.Empty(crm.Books);
    }

    // ── booking for someone else ──────────────────────────────────────────────

    private static Dictionary<string, System.Text.Json.JsonElement> ForOtherArgs(
        DateOnly day, string? forName, string? forWhatsApp)
    {
        var args = BookArgs(day, "16:00");
        if (forName is not null) args["for_name"] = System.Text.Json.JsonSerializer.SerializeToElement(forName);
        if (forWhatsApp is not null)
            args["for_whatsapp"] = System.Text.Json.JsonSerializer.SerializeToElement(forWhatsApp);
        return args;
    }

    [Fact]
    public async Task ForAnotherPerson_BooksUnderTheirNumberAndName_AndSaysWhoAskedInTheNote()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(880, day, "16:00", "16:45")),
        };

        var result = await Executor(kit, crm).ExecuteAsync(
            BotTools.BookAppointment, ForOtherArgs(day, "Piyal", "+971 50 765 4321"), default);

        Assert.True(result.Booked);
        Assert.StartsWith("Booked. PRP Consultation for Piyal", result.Content);
        Assert.Contains("goes to THIS chat, not to them", result.Content);   // never "sent to his number"

        var draft = Assert.Single(crm.Books);
        Assert.Equal("Piyal", draft.CustomerName);
        Assert.Equal("971507654321", draft.Phone);
        Assert.StartsWith("BOOKED FOR Piyal (+971507654321)", draft.Notes);
        Assert.Contains("Sarah Fernando", draft.Notes);

        // The chat's contact keeps its own name: Piyal is not written onto Sarah's record.
        Assert.Equal("Sarah Fernando", (await kit.Db.Contacts.AsNoTracking().SingleAsync()).Name);
    }

    [Fact]
    public async Task ForAnotherPerson_WithoutTheirNumber_IsRefused_AndNothingIsBooked()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);

        var result = await Executor(kit, crm).ExecuteAsync(
            BotTools.BookAppointment, ForOtherArgs(ClinicCrmTestKit.DaysAhead(3), "Piyal", null), default);

        Assert.False(result.Booked);
        Assert.Contains("BOTH their name and their own WhatsApp number", result.Content);
        Assert.Empty(crm.Books);
    }

    [Fact]
    public async Task ForAnotherPerson_GivenTheChattersOwnNumber_IsRefused_AndNothingIsBooked()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);

        var result = await Executor(kit, crm).ExecuteAsync(
            BotTools.BookAppointment, ForOtherArgs(ClinicCrmTestKit.DaysAhead(3), "Piyal", "+971501234567"), default);

        Assert.False(result.Booked);
        Assert.Contains("cannot be put under \"Piyal\"", result.Content);
        Assert.Empty(crm.Books);
    }

    [Fact]
    public async Task ForAnotherPerson_WhenTheClinicIsUnreachable_IsHandedToStaff_NotQueuedUnderTheChattersNumber()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Unavailable, Message: "The clinic's system could not be reached."),
        };

        var result = await Executor(kit, crm).ExecuteAsync(
            BotTools.BookAppointment, ForOtherArgs(day, "Piyal", "971507654321"), default);

        Assert.True(result.HandedOff);
        Assert.Contains("Do NOT say it is booked", result.Content);
        Assert.Empty(crm.Queued);
        Assert.False(await kit.Db.Appointments.AnyAsync());
        Assert.Contains(crm.Notifications, n => n.Body.Contains("971507654321"));
    }

    [Fact]
    public async Task Taken_ByTheCustomersOwnBooking_SaysSo_InsteadOfJustTaken()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        await SyncedBookingAsync(kit, day);   // theirs, at 16:00
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Taken, FreeTimes: ["15:00"]),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.False(result.Booked);
        Assert.Contains("already holds", result.Content);
        Assert.Contains("Do NOT say someone else took it", result.Content);
        Assert.DoesNotContain("just taken", result.Content);
        Assert.Contains($"start_local={day:yyyy-MM-dd}T15:00", result.Content);
    }

    [Fact]
    public async Task ABookingMadeForSomeoneElse_IsNotCancelledOrMovedFromThisChat()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SyncedBookingAsync(kit, day);
        booking.Notes = "BOOKED FOR Piyal (+971507654321) — requested in WhatsApp chat by Sarah.";
        kit.Db.Appointments.Update(booking);
        await kit.Db.SaveChangesAsync();
        kit.Db.ChangeTracker.Clear();
        var crm = new FakeClinicCrmBookings(kit);

        var cancel = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T16:00")), default);
        var move = await Executor(kit, crm).ExecuteAsync(BotTools.RescheduleAppointment, ClinicCrmTestKit.Args(
            ("from_start_local", $"{day:yyyy-MM-dd}T16:00"),
            ("to_start_local", $"{day:yyyy-MM-dd}T17:00"),
            ("to_weekday", day.DayOfWeek.ToString())), default);

        Assert.Contains("handoff_to_human", cancel.Content);
        Assert.Contains("handoff_to_human", move.Content);
        Assert.Empty(crm.CancellationRequests);
        Assert.Empty(crm.Moves);
        Assert.Equal(AppointmentStatus.Confirmed, (await kit.Db.Appointments.AsNoTracking().SingleAsync()).Status);
    }

    // ── check_availability ────────────────────────────────────────────────────

    [Fact]
    public async Task Availability_ComesFromTheClinic_DayByDay_SkippingClosedDays()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var first = ClinicCrmTestKit.DaysAhead(2);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnAvailability = d => d == first
                ? new(ClinicCrmOutcome.Ok, new ClinicCrmAvailability($"{d:yyyy-MM-dd}", true, "09:00", "18:00", 45, ["09:00", "16:00"]))
                : new(ClinicCrmOutcome.Ok, new ClinicCrmAvailability($"{d:yyyy-MM-dd}", false, null, null, 45, [])),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("from_date", $"{first:yyyy-MM-dd}"),
            ("to_date", $"{first.AddDays(2):yyyy-MM-dd}")), default);

        Assert.Equal([first, first.AddDays(1), first.AddDays(2)], crm.AvailabilityAsked);
        Assert.Contains($"start_local={first:yyyy-MM-dd}T09:00", result.Content);
        Assert.Contains($"start_local={first:yyyy-MM-dd}T16:00", result.Content);
    }

    [Fact]
    public async Task Availability_ShowsEveryFreeTime_NotJustTheFirstFew()
    {
        // The real case: 16 free times, the list stopped at 15:00, and the bot told the customer 16:00 worked.
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);
        string[] free = ["12:15", "12:30", "12:45", "13:00", "13:15", "13:30", "13:45", "14:00",
                         "14:15", "14:30", "14:45", "15:00", "15:15", "15:30", "16:45", "17:15"];
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnAvailability = d => new(ClinicCrmOutcome.Ok,
                new ClinicCrmAvailability($"{d:yyyy-MM-dd}", true, "12:00", "18:00", 45, free)),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("from_date", $"{day:yyyy-MM-dd}"),
            ("to_date", $"{day:yyyy-MM-dd}")), default);

        Assert.Contains("15:30, 16:45, 17:15", result.Content);
        Assert.DoesNotContain("16:00", result.Content);
        Assert.Contains("NOT free", result.Content);
    }

    private static FakeClinicCrmBookings CrmFreeAt(ClinicCrmTestKit kit, params string[] free) =>
        new(kit)
        {
            OnAvailability = d => new(ClinicCrmOutcome.Ok,
                new ClinicCrmAvailability($"{d:yyyy-MM-dd}", true, "12:00", "18:00", 45, free)),
        };

    [Fact]
    public async Task RequestedTime_ThatIsFree_IsStatedOutright_EvenWhenItIsPastTheTwelveDetailedLines()
    {
        // The real case (4 Oct): 22 free times 12:00-17:15, the detailed lines stopped at 14:45, and the
        // bot told the customer 17:00 was taken.
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);
        var free = Enumerable.Range(0, 22).Select(i => TimeOnly.Parse("12:00").AddMinutes(15 * i).ToString("HH:mm")).ToArray();

        var result = await Executor(kit, CrmFreeAt(kit, free)).ExecuteAsync(BotTools.CheckAvailability,
            ClinicCrmTestKit.Args(
                ("service_name", "PRP Consultation"),
                ("from_date", $"{day:yyyy-MM-dd}"),
                ("to_date", $"{day:yyyy-MM-dd}"),
                ("time", "17:00")), default);

        Assert.Contains("REQUESTED TIME 5:00 PM: FREE on", result.Content);
        Assert.Contains($"start_local={day:yyyy-MM-dd}T17:00", result.Content);
        Assert.DoesNotContain("NOT free in this range", result.Content);
    }

    [Fact]
    public async Task RequestedTime_ThatIsTaken_NamesTheClosestRealTimes_AndNothingElse()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);
        string[] free = ["12:15", "15:15", "15:30", "16:45", "17:15"];

        var result = await Executor(kit, CrmFreeAt(kit, free)).ExecuteAsync(BotTools.CheckAvailability,
            ClinicCrmTestKit.Args(
                ("service_name", "PRP Consultation"),
                ("from_date", $"{day:yyyy-MM-dd}"),
                ("to_date", $"{day:yyyy-MM-dd}"),
                ("time", "16:00")), default);

        Assert.Contains("REQUESTED TIME 4:00 PM: NOT free in this range", result.Content);
        Assert.Contains($"3:15 PM (start_local={day:yyyy-MM-dd}T15:15), 3:30 PM (start_local={day:yyyy-MM-dd}T15:30), 4:45 PM", result.Content);
        Assert.DoesNotContain("12:15 PM (start_local", result.Content.Split("Free slots for")[0]);
        Assert.DoesNotContain("REQUESTED TIME 4:00 PM: FREE", result.Content);
    }

    [Fact]
    public async Task NoRequestedTime_GivesNoVerdictLine()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);

        var result = await Executor(kit, CrmFreeAt(kit, "16:45", "17:15")).ExecuteAsync(BotTools.CheckAvailability,
            ClinicCrmTestKit.Args(
                ("service_name", "PRP Consultation"),
                ("from_date", $"{day:yyyy-MM-dd}"),
                ("to_date", $"{day:yyyy-MM-dd}")), default);

        Assert.DoesNotContain("REQUESTED TIME", result.Content);
    }

    [Fact]
    public async Task AnUnreadableRequestedTime_IsIgnored_AndTheSlotsAreStillReturned()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);

        var result = await Executor(kit, CrmFreeAt(kit, "16:45", "17:15")).ExecuteAsync(BotTools.CheckAvailability,
            ClinicCrmTestKit.Args(
                ("service_name", "PRP Consultation"),
                ("from_date", $"{day:yyyy-MM-dd}"),
                ("to_date", $"{day:yyyy-MM-dd}"),
                ("time", "five o'clock")), default);

        Assert.DoesNotContain("REQUESTED TIME", result.Content);
        Assert.Contains($"start_local={day:yyyy-MM-dd}T16:45", result.Content);
    }

    [Fact]
    public async Task Availability_SaysClosed_NotFullyBooked_ForADayTheClinicDoesNotOpen()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnAvailability = d => new(ClinicCrmOutcome.Ok,
                new ClinicCrmAvailability($"{d:yyyy-MM-dd}", false, null, null, 45, [])),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("from_date", $"{day:yyyy-MM-dd}"),
            ("to_date", $"{day:yyyy-MM-dd}")), default);

        Assert.Contains("CLOSED", result.Content);
        Assert.DoesNotContain("fully booked.", result.Content);
    }

    [Fact]
    public async Task Availability_SaysFullyBooked_ForAnOpenDayWithNoFreeTimes()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(2);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnAvailability = d => new(ClinicCrmOutcome.Ok,
                new ClinicCrmAvailability($"{d:yyyy-MM-dd}", true, "09:00", "18:00", 45, [])),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("from_date", $"{day:yyyy-MM-dd}"),
            ("to_date", $"{day:yyyy-MM-dd}")), default);

        Assert.Contains("open 09:00-18:00, but fully booked", result.Content);
        Assert.DoesNotContain("CLOSED", result.Content);
    }

    [Fact]
    public async Task Availability_AsksTheClinicForAtMostAWeek()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var first = ClinicCrmTestKit.DaysAhead(1);
        var crm = new FakeClinicCrmBookings(kit);

        await Executor(kit, crm).ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation"),
            ("from_date", $"{first:yyyy-MM-dd}"),
            ("to_date", $"{first.AddDays(30):yyyy-MM-dd}")), default);

        Assert.Equal(7, crm.AvailabilityAsked.Count);
    }

    [Fact]
    public async Task Availability_WhenTheClinicIsUnreachable_OffersNothing_RatherThanOurOwnCalendar()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnAvailability = _ => new(ClinicCrmOutcome.Unavailable),
        };

        // A null availability service would throw if the executor fell back to our calendar.
        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CheckAvailability, ClinicCrmTestKit.Args(
            ("service_name", "PRP Consultation")), default);

        Assert.Contains("could not be reached", result.Content);
        Assert.DoesNotContain("start_local", result.Content);
    }

    // ── cancel / reschedule ───────────────────────────────────────────────────

    private static async Task<Appointment> SyncedBookingAsync(ClinicCrmTestKit kit, DateOnly day, int bookingId = 873)
    {
        var start = ClinicCrmTestKit.DubaiToUtc(day, 16);
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), CompanyId = kit.CompanyId, ContactId = kit.Contact.Id,
            AppointmentTypeId = kit.Service.Id, ConversationId = kit.Conversation.Id,
            StartUtc = start, EndUtc = start.AddMinutes(45), BlockStartUtc = start, BlockEndUtc = start.AddMinutes(45),
            Status = AppointmentStatus.Confirmed, Source = AppointmentSource.Bot, IsActive = true,
            ExternalSyncStatus = ExternalSyncStatus.Synced, ExternalBookingId = bookingId,
        };
        kit.Db.Appointments.Add(appointment);
        await kit.Db.SaveChangesAsync();
        kit.Db.ChangeTracker.Clear();
        return appointment;
    }

    [Fact]
    public async Task BookingTheSameServiceAgain_IsRefused_AndPointsAtReschedule()
    {
        // Observed live: Monday was corrected to Thursday, Thursday was booked as a new appointment and
        // Monday stayed on the calendar.
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit);
        var existing = await SyncedBookingAsync(kit, ClinicCrmTestKit.DaysAhead(5));

        var result = await Executor(kit, crm).ExecuteAsync(
            BotTools.BookAppointment, BookArgs(ClinicCrmTestKit.DaysAhead(3), "16:00"), default);

        Assert.False(result.Booked);
        Assert.Contains("call reschedule_appointment", result.Content);
        Assert.Empty(crm.Books);
        Assert.NotNull(existing);
    }

    [Fact]
    public async Task CancellingAClinicBooking_CancelsItInTheCrm_AndOurs_WithNothingLeftForStaff()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnCancel = _ => new(ClinicCrmCancelOutcome.Cancelled), };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T16:00"), ("reason", "Travelling")), default);

        Assert.StartsWith("Cancelled.", result.Content);
        Assert.Equal((873, "971501234567", "Travelling"), Assert.Single(crm.CancellationRequests));

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booking.Id);
        Assert.Equal(AppointmentStatus.Cancelled, stored.Status);
        Assert.Equal(ExternalSyncStatus.Synced, stored.ExternalSyncStatus);   // cancelled on both sides
        Assert.Empty(crm.Notifications);
    }

    [Fact]
    public async Task CancellingAClinicBooking_FlagsItInTheCrm_CancelsOurs_AndTellsStaff()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnCancel = _ => new(ClinicCrmCancelOutcome.Flagged),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T16:00"), ("reason", "Travelling")), default);

        Assert.False(result.HandedOff);
        Assert.StartsWith("Cancelled.", result.Content);

        var request = Assert.Single(crm.CancellationRequests);
        Assert.Equal((873, "971501234567", "Travelling"), request);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booking.Id);
        Assert.Equal(AppointmentStatus.Cancelled, stored.Status);
        Assert.Equal(ExternalSyncStatus.CancelRequested, stored.ExternalSyncStatus);

        var (_, body) = Assert.Single(crm.Notifications);
        Assert.Contains("#873", body);
        Assert.Contains("+971501234567", body);

        var conversation = await kit.Db.Conversations.AsNoTracking().SingleAsync();
        Assert.Equal(ConversationBotState.Active, conversation.BotState);
    }

    [Fact]
    public async Task CancellingWhenTheClinicIsUnreachable_CancelsNothing_AndHandsItToAPerson()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnCancel = _ => new(ClinicCrmCancelOutcome.Unavailable),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T16:00")), default);

        Assert.True(result.HandedOff);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booking.Id);
        Assert.Equal(AppointmentStatus.Confirmed, stored.Status);
        Assert.Equal(ExternalSyncStatus.Synced, stored.ExternalSyncStatus);

        Assert.Equal("Cancellation request", Assert.Single(crm.Notifications).Title);
        var conversation = await kit.Db.Conversations.AsNoTracking().SingleAsync();
        Assert.Equal(ConversationBotState.Paused, conversation.BotState);
    }

    [Fact]
    public async Task MovingAClinicBooking_GoesThroughTheClinic_AndKeepsItsNewBookingId()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SyncedBookingAsync(kit, day);
        var newDay = day.AddDays(1);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnMove = (_, _) => new(ClinicCrmOutcome.Ok,
                ClinicCrmTestKit.Booking(874, newDay, "10:30", "11:15", action: "rescheduled")),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.RescheduleAppointment, ClinicCrmTestKit.Args(
            ("from_start_local", $"{day:yyyy-MM-dd}T16:00"),
            ("to_start_local", $"{newDay:yyyy-MM-dd}T10:30")), default);

        Assert.StartsWith("Moved.", result.Content);

        var move = Assert.Single(crm.Moves);
        Assert.Equal(873, move.BookingId);
        Assert.Equal("971501234567", move.Phone);
        Assert.Equal(ClinicCrmTestKit.DubaiToUtc(newDay, 10, 30), move.StartUtc);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booking.Id);
        Assert.Equal(874, stored.ExternalBookingId);
        Assert.Equal(ClinicCrmTestKit.DubaiToUtc(newDay, 10, 30), stored.StartUtc);
        Assert.Equal(ClinicCrmTestKit.DubaiToUtc(newDay, 11, 15), stored.EndUtc);
    }

    [Fact]
    public async Task AMoveTheClinicRefusesAsTaken_LeavesTheBookingWhereItWas()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var booking = await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnMove = (_, _) => new(ClinicCrmOutcome.Taken, FreeTimes: ["12:00"]),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.RescheduleAppointment, ClinicCrmTestKit.Args(
            ("from_start_local", $"{day:yyyy-MM-dd}T16:00"),
            ("to_start_local", $"{day.AddDays(1):yyyy-MM-dd}T10:30")), default);

        Assert.Contains("Not moved", result.Content);
        Assert.Contains($"start_local={day.AddDays(1):yyyy-MM-dd}T12:00", result.Content);

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booking.Id);
        Assert.Equal(873, stored.ExternalBookingId);
        Assert.Equal(booking.StartUtc, stored.StartUtc);
    }

    // ── bookings made by phone (only in the clinic's CRM) ─────────────────────

    private static ClinicCrmBooking PhoneBooking(int id, DateOnly day, string start, string end, string treatment = "Hair Treatment") =>
        new(id, 1052, "Sarah Fernando", $"{day:yyyy-MM-dd}", start, end,
            [new ClinicCrmBookingTreatment(3, treatment)], "Booked", null);

    [Fact]
    public async Task FindAppointments_ListsPhoneBookingsAlongsideChatOnes_SayingWhichIsWhich()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit);
        crm.PhoneBookings.Add(PhoneBooking(900, day.AddDays(1), "11:00", "11:45"));

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.FindAppointments, ClinicCrmTestKit.Args(), default);

        Assert.Contains($"start_local={day:yyyy-MM-dd}T16:00", result.Content);
        Assert.Contains($"start_local={day.AddDays(1):yyyy-MM-dd}T11:00", result.Content);
        Assert.Contains("booked through this chat", result.Content);
        Assert.Contains("booked directly with the clinic", result.Content);
    }

    [Fact]
    public async Task CancellingAPhoneBooking_CancelsItAtTheClinic()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit) { OnCancel = _ => new(ClinicCrmCancelOutcome.Cancelled), };
        crm.PhoneBookings.Add(PhoneBooking(900, day, "11:00", "11:45"));

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T11:00")), default);

        Assert.Contains("cancelled at the clinic", result.Content);
        Assert.Equal(900, Assert.Single(crm.CancellationRequests).BookingId);
        Assert.Empty(crm.Notifications);
    }

    [Fact]
    public async Task APhoneBookingTheClinicWillNotCancel_IsFlagged_AndStaffAreTold()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnCancel = _ => new(ClinicCrmCancelOutcome.Flagged),
        };
        crm.PhoneBookings.Add(PhoneBooking(900, day, "11:00", "11:45"));

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T11:00"), ("reason", "Feeling unwell")), default);

        Assert.StartsWith("Cancelled.", result.Content);
        Assert.Equal((900, "971501234567", "Feeling unwell"), Assert.Single(crm.CancellationRequests));
        Assert.Contains("booked directly with the clinic", Assert.Single(crm.Notifications).Body);
        Assert.False(await kit.Db.Appointments.AnyAsync());      // nothing of ours to touch
    }

    [Fact]
    public async Task CancellingEveryPrpBooking_ReachesBothThePhoneOneAndTheChatOne()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var chatBooking = await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnCancel = _ => new(ClinicCrmCancelOutcome.Cancelled), };
        crm.PhoneBookings.Add(PhoneBooking(900, day.AddDays(2), "10:00", "10:45"));
        var executor = Executor(kit, crm);

        // What the model does after "cancel all my PRP": one call per booking, in the same turn.
        var first = await executor.ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T16:00")), default);
        var second = await executor.ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day.AddDays(2):yyyy-MM-dd}T10:00")), default);

        Assert.StartsWith("Cancelled.", first.Content);
        Assert.StartsWith("Cancelled.", second.Content);
        Assert.Equal([873, 900], crm.CancellationRequests.Select(r => r.BookingId));

        var stored = await kit.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == chatBooking.Id);
        Assert.Equal(AppointmentStatus.Cancelled, stored.Status);
        Assert.Empty(crm.Notifications);
    }

    [Fact]
    public async Task MovingAPhoneBooking_GoesThroughTheClinic()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnMove = (_, _) => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(901, day, "14:00", "14:45", "rescheduled")),
        };
        crm.PhoneBookings.Add(PhoneBooking(900, day, "11:00", "11:45"));

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.RescheduleAppointment, ClinicCrmTestKit.Args(
            ("from_start_local", $"{day:yyyy-MM-dd}T11:00"),
            ("to_start_local", $"{day:yyyy-MM-dd}T14:00")), default);

        Assert.StartsWith("Moved.", result.Content);
        Assert.Equal(900, Assert.Single(crm.Moves).BookingId);
        Assert.False(await kit.Db.Appointments.AnyAsync());
    }

    [Fact]
    public async Task AChatBookingTheClinicNoLongerLists_IsNotOfferedAsUpcoming()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit)
        {
            // The clinic cancelled it in the CRM.
            OnUpcoming = () => new(ClinicCrmOutcome.Ok, Array.Empty<ClinicCrmBooking>()),
        };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.CancelAppointment, ClinicCrmTestKit.Args(
            ("start_local", $"{day:yyyy-MM-dd}T16:00")), default);

        Assert.Contains("no upcoming appointments", result.Content);
        Assert.Empty(crm.CancellationRequests);
    }

    [Fact]
    public async Task WhenTheClinicIsUnreachable_OurBookingsAreShown_WithAWarningThatPhoneOnesMayBeMissing()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        await SyncedBookingAsync(kit, day);
        var crm = new FakeClinicCrmBookings(kit) { OnUpcoming = () => new(ClinicCrmOutcome.Unavailable) };

        var result = await Executor(kit, crm).ExecuteAsync(BotTools.FindAppointments, ClinicCrmTestKit.Args(), default);

        Assert.Contains($"start_local={day:yyyy-MM-dd}T16:00", result.Content);
        Assert.Contains("may be missing", result.Content);
    }

    // ── details first, then the customer's yes ───────────────────────────────

    private static BotToolExecutor ExecutorAfter(
        ClinicCrmTestKit kit, FakeClinicCrmBookings crm, params AiTranscriptTurn[] transcript) =>
        new(kit.Db, null!, kit.Appointments(crm),
            new BotToolScope(
                kit.CompanyId, kit.Contact.Id, kit.Conversation.Id, ClinicCrmTestKit.Dubai, DryRun: false,
                Transcript: transcript),
            NullLogger<BotToolExecutor>.Instance,
            crm);

    [Fact]
    public async Task ABookingTheCustomerWasNeverShown_IsRefused_AndNothingReachesTheClinic()
    {
        // The owner's rule: send the details, wait for their yes, then book. "ok 4pm works" to a list of
        // times is agreeing to a time, not confirming a booking.
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(873, day, "16:00", "16:45")),
        };

        var result = await ExecutorAfter(kit, crm,
                new AiTranscriptTurn(false, "We have 3:00 PM or 4:00 PM free. Which suits you?"),
                new AiTranscriptTurn(true, "4pm is fine, I'm Sarah"))
            .ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.False(result.Booked);
        Assert.Contains("Not booked yet", result.Content);
        Assert.Contains("Shall I book it?", result.Content);
        Assert.Empty(crm.Books);
    }

    [Fact]
    public async Task AYesToTheDetails_Books()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit)
        {
            OnBook = _ => new(ClinicCrmOutcome.Ok, ClinicCrmTestKit.Booking(873, day, "16:00", "16:45")),
        };
        var shown = new DateTime(day.Year, day.Month, day.Day, 16, 0, 0);

        var result = await ExecutorAfter(kit, crm,
                new AiTranscriptTurn(false,
                    $"Just to confirm: PRP Consultation for Sarah on {shown:ddd d MMM, h:mm tt}. Shall I book it?"),
                new AiTranscriptTurn(true, "yes"))
            .ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.True(result.Booked);
        Assert.Single(crm.Books);
    }

    [Fact]
    public async Task AFollowUpCanNeverBook_TheCustomerHasNotAnswered()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var day = ClinicCrmTestKit.DaysAhead(3);
        var crm = new FakeClinicCrmBookings(kit);
        var shown = new DateTime(day.Year, day.Month, day.Day, 16, 0, 0);

        var result = await ExecutorAfter(kit, crm,
                new AiTranscriptTurn(false, $"Just to confirm: {shown:ddd d MMM, h:mm tt}. Shall I book it?"))
            .ExecuteAsync(BotTools.BookAppointment, BookArgs(day, "16:00"), default);

        Assert.False(result.Booked);
        Assert.Empty(crm.Books);
    }
}
