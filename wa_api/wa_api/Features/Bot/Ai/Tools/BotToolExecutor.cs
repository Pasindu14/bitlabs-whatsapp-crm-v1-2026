using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Calendar;
using wa_api.Features.Calendar.Dtos;
using wa_api.Features.Calendar.Entities;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.Bot.StaffAlerts;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Conversations.Entities;
using wa_api.Features.Messages.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.Ai.Tools;

/// <summary>
/// Which thread the bot is acting on, and what it is allowed to book there.
/// <para>
/// <see cref="DryRun"/> is the preview mode: availability is read for real so a tenant can see the bot
/// quoting genuine times, but nothing is written. Without it, tuning the persona would mean putting
/// real appointments on a real calendar.
/// </para>
/// </summary>
public sealed record BotToolScope(
    Guid CompanyId,
    Guid ContactId,
    Guid? ConversationId,
    TimeZoneInfo TimeZone,
    bool DryRun,
    string? CustomerText = null,
    /// <summary>
    /// The chat so far, ending with the customer's latest message. When set, a booking is only made once
    /// the customer has answered a message that showed its details (<see cref="BookingConfirmation"/>).
    /// Every real caller sets it; only unit tests of other behaviour leave it null.
    /// </summary>
    IReadOnlyList<AiTranscriptTurn>? Transcript = null
);

/// <summary>
/// Runs the bot's tools against the real calendar module.
/// <para>
/// This class does no availability arithmetic and no overlap checking of its own — it resolves what the
/// model asked for into ids and instants, then calls <see cref="IAvailabilityService"/> and
/// <see cref="IAppointmentService"/>. That is the whole safety argument for the engine: the model
/// cannot conjure a slot, because the same code path that refuses a bad staff booking refuses a bad
/// bot booking, right down to the database's exclusion constraint.
/// </para>
/// <para>
/// Nothing here throws. Every refusal comes back as text the model can act on, because "that time was
/// just taken, pick another" is a conversation the bot can have and an exception is a customer left on
/// read after saying yes.
/// </para>
/// <para>
/// <b>Clinic CRM (plan 014).</b> When the company is connected to its clinic's CRM, that CRM is the
/// calendar that counts: free times come from it, a booking is made there FIRST and only mirrored here
/// once it says yes, and moves go through it. Without a connection, or with no <paramref name="crm"/>
/// passed, every tool behaves exactly as before.
/// </para>
/// </summary>
public sealed class BotToolExecutor(
    AppDbContext db,
    IAvailabilityService availability,
    IAppointmentService appointments,
    BotToolScope scope,
    ILogger<BotToolExecutor> logger,
    IClinicCrmBookingService? crm = null,
    IStaffAlertService? staffAlerts = null) : IBotToolExecutor
{
    /// <summary>
    /// Slots offered per call. A month of 15-minute slots would swamp the context and the model reads a
    /// long list badly — a dozen is more than any customer wants to choose between anyway.
    /// </summary>
    private const int MaxSlots = 12;

    /// <summary>Default search window when the model does not name one.</summary>
    private const int DefaultHorizonDays = 7;

    public async Task<BotToolResult> ExecuteAsync(
        string toolName, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                BotTools.CheckAvailability => await CheckAvailabilityAsync(input, ct),
                BotTools.BookAppointment => await BookAsync(input, ct),
                BotTools.FindAppointments => await FindAppointmentsAsync(ct),
                BotTools.CancelAppointment => await CancelAsync(input, ct),
                BotTools.RescheduleAppointment => await RescheduleAsync(input, ct),
                BotTools.HandoffToHuman => await HandoffAsync(input, ct),
                BotTools.ShareLink => await ShareLinkAsync(input, ct),
                _ => new BotToolResult($"There is no tool called {toolName}."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A tool that throws would kill the whole reply. Telling the model the tool failed lets it
            // apologise and offer a person instead, which is what a colleague would do.
            logger.LogError(ex, "Bot tool {Tool} failed for conversation {ConversationId}",
                toolName, scope.ConversationId);

            return new BotToolResult(
                "That did not work just now. Do not try it again — apologise briefly and offer to have "
                + "someone follow up.");
        }
    }

    // ── check_availability ────────────────────────────────────────────────────

    private async Task<BotToolResult> CheckAvailabilityAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var types = await BookableTypesAsync(ct);
        if (types.Count == 0)
            return new BotToolResult(
                "This business has no bookable services set up. Do not offer to book anything.");

        var requested = ReadString(input, "service_name");
        var resolved = ResolveType(types, requested);

        if (resolved is null)
        {
            // Ambiguous or unknown: hand the model the real list rather than let it guess. This is the
            // most common way a booking goes wrong, and it is recoverable with one question.
            var names = string.Join(", ", types.Select(t => t.Name));
            return new BotToolResult(requested is null
                ? $"Which service? The options are: {names}. Ask the customer to pick one."
                : $"There is no service called \"{requested}\". The options are: {names}. "
                  + "Ask the customer which one they mean.");
        }

        var today = CompanyTimeZone.TodayIn(scope.TimeZone, DateTime.UtcNow);
        var from = ReadDate(input, "from_date") ?? today;
        if (from < today) from = today;
        var to = ReadDate(input, "to_date") ?? from.AddDays(DefaultHorizonDays);
        if (to < from) to = from;

        var wanted = ReadTime(input, "time");

        // A single-day lookup is the model resolving the customer's "Saturday" or "Thursday" into a date, and
        // that is where it slipped (Saturday → 7 Nov instead of 31 Oct). A range is a browse, so left alone.
        if (from == to && CustomerNamedOtherDay(from.ToDateTime(TimeOnly.MinValue)) is { } otherDay)
            return new BotToolResult(otherDay);

        if (await ClinicEndpointAsync(ct) is { } endpoint)
            return await CheckClinicAvailabilityAsync(endpoint, resolved, from, to, today, wanted, ct);

        AvailabilityResponse result;
        try
        {
            result = await availability.GetAsync(resolved.Id, from, to, ct);
        }
        catch (AppException ex)
        {
            return new BotToolResult(ex.Message);
        }

        if (result.Slots.Count == 0)
            return new BotToolResult(
                $"No free slots for {resolved.Name} between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}. "
                + "Tell the customer and offer to look at a different period.");

        return new BotToolResult(
            DescribeSlots(resolved.Name, [.. result.Slots.Select(s => s.StartUtc)], today, wanted));
    }

    /// <summary>
    /// The slot list the model books from. Shared by our calendar and the clinic's, so the model sees
    /// the same shape — and passes back the same <c>start_local</c> — whichever one answered.
    /// </summary>
    private string DescribeSlots(
        string serviceName, IReadOnlyList<DateTime> startsUtc, DateOnly today, TimeOnly? wanted = null)
    {
        var sb = new StringBuilder();

        if (wanted is { } time) sb.Append(DescribeRequestedTime(startsUtc, time));

        sb.Append("Free slots for ").Append(serviceName).AppendLine(":");

        foreach (var startUtc in startsUtc.Take(MaxSlots))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, scope.TimeZone);
            sb.Append("- ").Append(Describe(local))
              .Append("  (pass start_local=").Append(local.ToString("yyyy-MM-dd'T'HH:mm", Invariant))
              .AppendLine(" to book this one)");
        }

        if (startsUtc.Count > MaxSlots)
        {
            // The detailed lines stop at MaxSlots, but the model must still see every free time: a customer
            // asking for 4 PM when the list ends at 3 PM was told "4 PM works" for a time that was taken.
            sb.AppendLine("Every free start time (complete list; book any of them with start_local=<date>T<time>):");
            foreach (var day in startsUtc
                         .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s, scope.TimeZone))
                         .GroupBy(l => DateOnly.FromDateTime(l)))
            {
                sb.Append("- ").Append(day.Key.ToString("ddd d MMM (yyyy-MM-dd)", Invariant)).Append(": ")
                  .AppendLine(string.Join(", ", day.Select(l => l.ToString("HH:mm", Invariant))));
            }
        }

        sb.AppendLine("A time that is not in this result is NOT free. If the customer asks for one, say it is "
            + "taken and offer the nearest free times — never say it is available.");
        sb.AppendLine("Offer two or three of these, not the whole list.");

        // Real counts, so the model can say "two left today" truthfully instead of either staying
        // vague or inventing pressure. "...and 68 more" told it there was plenty but not how much was
        // left TODAY, which is the number a customer actually reacts to. Costs nothing — these are the
        // slots already in hand.
        var localDates = startsUtc
            .Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s, scope.TimeZone)))
            .ToList();

        var leftToday = localDates.Count(d => d == today);
        var leftTomorrow = localDates.Count(d => d == today.AddDays(1));

        sb.Append("Actual counts: ").Append(leftToday).Append(" left today, ")
          .Append(leftTomorrow).Append(" tomorrow, ")
          .Append(startsUtc.Count).AppendLine(" in the range you asked for.");
        sb.AppendLine("You may quote these numbers exactly. Do not dress them up — no \"filling fast\" or");
        sb.AppendLine("\"book soon\" unless the number really is small.");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The verdict on the time the customer named, worked out here instead of left for the model to find in
    /// a list of twenty. Read off such a list, 5 PM was reported taken when it was free, and a 5:30 that was
    /// never in the list was offered in its place. When it is not free, the nearest real times are named so
    /// the model has nothing to invent.
    /// </summary>
    private string DescribeRequestedTime(IReadOnlyList<DateTime> startsUtc, TimeOnly wanted)
    {
        const string slotFormat = "yyyy-MM-dd'T'HH:mm";

        var slots = startsUtc
            .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s, scope.TimeZone))
            .OrderBy(l => l)
            .ToList();

        var label = wanted.ToString("h:mm tt", Invariant);
        var exact = slots.Where(l => TimeOnly.FromDateTime(l) == wanted).ToList();

        if (exact.Count > 0)
        {
            var days = string.Join(", ", exact.Select(l =>
                $"{l.ToString("ddd d MMM", Invariant)} (start_local={l.ToString(slotFormat, Invariant)})"));

            return $"REQUESTED TIME {label}: FREE on {days}. Offer that exact time first.\n";
        }

        // Day by day, the three closest to what they asked for, so the model offers real times near it.
        var nearest = slots
            .GroupBy(l => DateOnly.FromDateTime(l))
            .Take(3)
            .Select(day => day.Key.ToString("ddd d MMM", Invariant) + ": " + string.Join(", ", day
                .OrderBy(l => Math.Abs((l.TimeOfDay - wanted.ToTimeSpan()).TotalMinutes))
                .Take(3)
                .OrderBy(l => l)
                .Select(l => $"{l.ToString("h:mm tt", Invariant)} (start_local={l.ToString(slotFormat, Invariant)})")));

        return $"REQUESTED TIME {label}: NOT free in this range. Closest free times — {string.Join("; ", nearest)}. "
            + "Tell the customer that time is taken and offer these. Never offer a time that is not listed here.\n";
    }

    // ── check_availability against the clinic's CRM ───────────────────────────

    /// <summary>Days asked of the CRM per call — one request each, against a 120-a-minute limit.</summary>
    private const int MaxClinicDays = 7;

    /// <summary>
    /// The clinic's own free times, day by day. Our calendar is deliberately NOT consulted as a fallback
    /// when the CRM cannot be reached: offering times the clinic may not have is exactly the failure this
    /// integration exists to prevent.
    /// </summary>
    private async Task<BotToolResult> CheckClinicAvailabilityAsync(
        ClinicCrmEndpoint endpoint, AppointmentType type, DateOnly from, DateOnly to, DateOnly today,
        TimeOnly? wanted, CancellationToken ct)
    {
        var last = from.AddDays(MaxClinicDays - 1);
        if (to > last) to = last;

        var starts = new List<DateTime>();
        string? refusal = null;

        // Closed and fully booked are different answers to a customer: "we're closed Fridays" is not
        // "Friday is full". Keep them apart, with the clinic's own hours, so the model never merges them.
        var closed = new List<DateOnly>();
        var full = new List<(DateOnly Date, string? Hours)>();
        var openHours = new List<(DateOnly Date, string Hours)>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var day = await crm!.GetAvailabilityAsync(endpoint, date, ct);

            if (day.Outcome is ClinicCrmOutcome.Unavailable or ClinicCrmOutcome.NotAuthorised)
            {
                if (starts.Count > 0) break;      // show what we already have rather than nothing
                return new BotToolResult(
                    "The clinic's booking system could not be reached just now, so no times can be offered. "
                    + "Apologise briefly and offer to have someone from the clinic get back to them with times.");
            }

            if (!day.IsOk)
            {
                refusal ??= day.Message;          // e.g. a date more than 180 days ahead
                continue;
            }

            if (day.Data is not { } data) continue;

            if (!data.Open)
            {
                closed.Add(date);
                continue;
            }

            var hours = data.OpensAt is not null && data.ClosesAt is not null ? $"{data.OpensAt}-{data.ClosesAt}" : null;
            var before = starts.Count;

            foreach (var time in data.FreeTimes ?? [])
            {
                if (ClinicCrmFormat.ToUtc(data.Date ?? ClinicCrmFormat.Date(date), time, crm.CrmTimeZone) is { } utc
                    && utc > DateTime.UtcNow)
                    starts.Add(utc);
            }

            if (starts.Count == before) full.Add((date, hours));
            else if (hours is not null) openHours.Add((date, hours));
        }

        var days = DescribeClinicDays(closed, full, openHours);

        if (starts.Count == 0)
            return new BotToolResult(refusal is not null && closed.Count == 0 && full.Count == 0
                ? $"No times available: {refusal} Tell the customer and offer to look at a different period."
                : $"No free times for {type.Name} at the clinic between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.\n"
                  + days + "Tell the customer exactly that — closed is not the same as fully booked — and offer to "
                  + "look at a different period.");

        starts.Sort();
        return new BotToolResult(days + DescribeSlots(type.Name, starts, today, wanted));
    }

    /// <summary>
    /// The clinic's answer per day. It is final: it overrides any opening hours the model has seen elsewhere.
    /// </summary>
    private static string DescribeClinicDays(
        List<DateOnly> closed, List<(DateOnly Date, string? Hours)> full, List<(DateOnly Date, string Hours)> open)
    {
        if (closed.Count == 0 && full.Count == 0 && open.Count == 0) return "";

        static string Day(DateOnly d) => d.ToString("dddd d MMM yyyy", Invariant);

        var sb = new StringBuilder("The clinic's own calendar says (this is final):\n");
        foreach (var d in closed)
            sb.Append("- ").Append(Day(d)).AppendLine(": CLOSED. The clinic does not open that day.");
        foreach (var (d, hours) in full)
            sb.Append("- ").Append(Day(d)).Append(": open").Append(hours is null ? "" : $" {hours}")
              .AppendLine(", but fully booked.");
        foreach (var (d, hours) in open)
            sb.Append("- ").Append(Day(d)).Append(": open ").AppendLine(hours);
        return sb.ToString();
    }

    private ClinicCrmEndpoint? _clinicEndpoint;
    private bool _clinicEndpointLoaded;

    /// <summary>The company's clinic CRM, or null to use our own calendar. Read once per reply.</summary>
    private async Task<ClinicCrmEndpoint?> ClinicEndpointAsync(CancellationToken ct)
    {
        if (crm is null) return null;
        if (_clinicEndpointLoaded) return _clinicEndpoint;

        _clinicEndpoint = await crm.GetEndpointAsync(ct);
        _clinicEndpointLoaded = true;
        return _clinicEndpoint;
    }

    /// <summary>
    /// The CRM's suggested free times after a 409, as <c>start_local</c> values the model can book straight
    /// away. The CRM gives times for the requested day only, in its own zone.
    /// </summary>
    private string OtherClinicTimes(DateTime requestedUtc, IReadOnlyList<string>? freeTimes)
    {
        if (freeTimes is not { Count: > 0 }) return "Use check_availability again and offer a different time.";

        var (date, _) = ClinicCrmFormat.FromUtc(requestedUtc, crm!.CrmTimeZone);

        var options = freeTimes
            .Select(t => ClinicCrmFormat.ToUtc(date, t, crm.CrmTimeZone))
            .OfType<DateTime>()
            .Where(utc => utc > DateTime.UtcNow)
            .Select(utc => TimeZoneInfo.ConvertTimeFromUtc(utc, scope.TimeZone))
            .Select(local => $"- {Describe(local)}  (start_local={local.ToString("yyyy-MM-dd'T'HH:mm", Invariant)})")
            .ToList();

        // All of them, not the first few: one day is short, and the times nearest the one they asked for are
        // the ones worth offering — a cut-off list hid exactly those.
        return options.Count == 0
            ? "Use check_availability again and offer a different time."
            : "Still free that day (complete list):\n" + string.Join("\n", options)
              + "\nOffer the two or three closest to the time they asked for.";
    }

    // ── book_appointment ──────────────────────────────────────────────────────

    private async Task<BotToolResult> BookAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var types = await BookableTypesAsync(ct);
        var requested = ReadString(input, "service_name");
        var resolved = ResolveType(types, requested);

        if (resolved is null)
        {
            var names = string.Join(", ", types.Select(t => t.Name));
            return new BotToolResult(
                $"Cannot book: \"{requested}\" is not one of the services. The options are: {names}.");
        }

        var startLocalText = ReadString(input, "start_local");
        if (startLocalText is null)
            return new BotToolResult("Cannot book without start_local. Confirm the time with the customer first.");

        if (!TryParseLocal(startLocalText, out var startLocal))
            return new BotToolResult(
                $"\"{startLocalText}\" is not a time I can read. Use exactly YYYY-MM-DDTHH:MM.");

        if (WeekdayMismatch(ReadString(input, "weekday"), startLocal) is { } wrongDay)
            return new BotToolResult(wrongDay);

        if (CustomerNamedOtherDay(startLocal) is { } otherDay)
            return new BotToolResult(otherDay);

        DateTime startUtc;
        try
        {
            startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, scope.TimeZone);
        }
        catch (ArgumentException)
        {
            // The local clock skipped this instant (a DST spring-forward gap). Rare, but a real time
            // the customer cannot attend, so it must be refused rather than nudged an hour.
            return new BotToolResult(
                "That local time does not exist on that date because the clocks change. Offer a different time.");
        }

        var customerName = ReadString(input, "customer_name");
        if (string.IsNullOrWhiteSpace(customerName))
            return new BotToolResult("Cannot book without the customer's name. Ask for it first.");

        var (other, otherError) = await ReadOtherPersonAsync(input, ct);
        if (otherError is not null) return new BotToolResult(otherError);

        // Who the appointment is FOR: the chatter, or the other person they named.
        var patientName = other?.Name ?? customerName;

        // The owner's rule: details first, the customer's yes, then the booking. Same for our calendar and
        // the clinic's CRM, so it sits before either is touched.
        if (scope.Transcript is { } transcript
            && !BookingConfirmation.Shows(BookingConfirmation.LastBotMessageBeforeReply(transcript), startLocal))
        {
            return new BotToolResult(
                "Not booked yet: the customer has not confirmed these details. Send them the details and ask "
                + $"them to confirm, e.g. \"Just to confirm: {resolved.Name} for {patientName} on "
                + $"{Describe(startLocal)}. Shall I book it?\" Do NOT say it is booked. Once they say yes, call "
                + "book_appointment again with the same details.");
        }

        if (scope.DryRun)
            return new BotToolResult(
                $"[preview] Would book {resolved.Name} for {patientName} on {Describe(startLocal)}. "
                + "Nothing was actually booked. Confirm it to the customer as if it succeeded.",
                Booked: true);

        if (scope.ConversationId is not { } conversationId)
            return new BotToolResult("Cannot book from this conversation.");

        // Observed live: a customer corrected "Monday" to "Thursday" and the bot booked Thursday as a NEW
        // appointment, leaving the Monday one on the calendar. Someone changing their mind has one booking,
        // not two. Booking for another person is a different visit, so it is not held to this, and the same
        // start time is left to the calendar, which already tells them it is their own booking.
        if (other is null
            && (await UpcomingAsync(ct)).FirstOrDefault(
                a => a.AppointmentTypeId == resolved.Id && a.StartUtc != startUtc) is { } existing)
        {
            var existingLocal = TimeZoneInfo.ConvertTimeFromUtc(existing.StartUtc, scope.TimeZone);
            return new BotToolResult(
                $"Not booked: they already have {resolved.Name} on {Describe(existingLocal)}. If they are changing "
                + "the day or time, call reschedule_appointment with from_start_local="
                + $"{existingLocal.ToString("yyyy-MM-dd'T'HH:mm", Invariant)} and the new time instead of booking "
                + "a second one. Only if they want a second, separate visit, ask them to confirm that first.");
        }

        if (await ClinicEndpointAsync(ct) is { } endpoint)
            return await BookWithClinicAsync(
                endpoint, resolved, startUtc, startLocal, customerName, other, ReadString(input, "notes"),
                conversationId, ct);

        try
        {
            var notes = ReadString(input, "notes");
            if (other is not null)
            {
                var chatter = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
                notes = LimitNotes(BookedForLabel(other, chatter, customerName) + (notes is null ? "" : "\n" + notes));
            }

            var appointment = await appointments.CreateAsync(
                new CreateAppointmentRequest(
                    ContactId: scope.ContactId,
                    AppointmentTypeId: resolved.Id,
                    StartUtc: startUtc,
                    Notes: notes,
                    ConversationId: conversationId),
                AppointmentSource.Bot,
                createdByUserId: null,
                ct);

            await BackfillContactNameAsync(customerName, ct);

            logger.LogInformation(
                "Bot booked appointment {AppointmentId} on conversation {ConversationId}",
                appointment.Id, conversationId);

            return new BotToolResult(
                $"Booked. {resolved.Name} for {patientName} on {Describe(startLocal)}. " + BookedReply,
                Booked: true);
        }
        catch (AppException ex)
        {
            // SLOT_NOT_AVAILABLE (the calendar would never have offered it) and SLOT_TAKEN (someone
            // else won the race) both land here. Both are recoverable in conversation.
            logger.LogInformation(
                "Bot booking refused on conversation {ConversationId}: {ErrorCode}", conversationId, ex.ErrorCode);

            return new BotToolResult(
                $"Not booked: {ex.Message} Apologise briefly, then use check_availability again and "
                + "offer a different time.");
        }
    }

    private const string BookedReply =
        "A confirmation with the service, date, time and place is sent to the customer automatically right "
        + "after your reply — do not repeat those details. Reply with one short, warm line (e.g. \"You're all "
        + "set, see you then!\") plus anything they should bring or know before the visit.";

    /// <summary>
    /// Books in the clinic's CRM first, and mirrors it here only once the CRM has said yes — so the
    /// customer is never told "booked" for a time the clinic then refuses.
    /// </summary>
    private async Task<BotToolResult> BookWithClinicAsync(
        ClinicCrmEndpoint endpoint, AppointmentType type, DateTime startUtc, DateTime startLocal,
        string customerName, OtherPerson? other, string? notes, Guid conversationId, CancellationToken ct)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        if (contact is null) return new BotToolResult("Cannot book from this conversation.");

        // The name they typed for this booking, not the WhatsApp profile name: observed live, "Charlie" was
        // sent to the clinic as the profile's "Ch". Someone else is that customer under THEIR number: the
        // clinic keys people by WhatsApp number, so that is the only way a booking can carry another name.
        var name = other?.Name ?? customerName.Trim();
        var phone = other?.Phone ?? contact.Phone;

        // The handover note the clinic asked for: concern, discussed, recommended — from the whole chat.
        var summary = await crm!.SummarizeConversationAsync(scope.CompanyId, conversationId, type.Name, ct);
        var bookingNotes = CombineNotes(summary, notes);

        // Who really asked, up front where a length cut will not take it.
        if (other is not null)
            bookingNotes = LimitNotes(BookedForLabel(other, contact, customerName)
                + (bookingNotes is null ? "" : "\n" + bookingNotes));

        var result = await crm.BookAsync(
            endpoint,
            new ClinicCrmBookingDraft(name, phone, type.ExternalTreatmentId, startUtc, bookingNotes),
            ct);

        var request = new CreateAppointmentRequest(scope.ContactId, type.Id, startUtc, bookingNotes, conversationId);

        switch (result.Outcome)
        {
            case ClinicCrmOutcome.Ok when result.Data is { } change:
            {
                var booking = change.Booking;
                var endUtc = ClinicCrmFormat.ToUtc(booking.Date, booking.EndTime, crm.CrmTimeZone);

                try
                {
                    await appointments.CreateMirrorAsync(request, endUtc,
                        new ExternalBookingLink(ExternalSyncStatus.Synced, booking.BookingId, booking.CustomerId), ct);
                }
                catch (AppException ex)
                {
                    // The clinic has the booking — that is what matters to the customer. Our calendar has
                    // something overlapping it, which a person needs to look at.
                    logger.LogError(
                        "Clinic CRM booking {BookingId} could not be mirrored on conversation {ConversationId}: {Error}",
                        booking.BookingId, conversationId, ex.Message);

                    await crm.NotifyStaffAsync(scope.CompanyId, "Clinic booking not on our calendar",
                        $"The clinic CRM booked {name} (+{phone}) for {type.Name} on {Describe(startLocal)} "
                        + $"(clinic booking #{booking.BookingId}), but our calendar already has something at that "
                        + "time, so it is not shown there. Please check the calendar.", ct);

                    await BackfillContactNameAsync(customerName, ct);

                    return new BotToolResult(
                        $"Booked at the clinic. {type.Name} for {name} on {Describe(startLocal)}. No automatic "
                        + "confirmation will follow this time, so state the service, date and time in your reply.",
                        Booked: true);
                }

                await BackfillContactNameAsync(customerName, ct);

                logger.LogInformation(
                    "Bot booked clinic CRM booking {BookingId} on conversation {ConversationId}",
                    booking.BookingId, conversationId);

                return new BotToolResult(
                    $"Booked. {type.Name} for {name} on {Describe(startLocal)}. " + BookedReply
                    + (other is null ? "" : $" The booking is under {other.Name}'s own number, but the confirmation "
                        + "message goes to THIS chat, not to them — never say it was sent to their number; if you "
                        + "mention it, tell the customer to forward it to them. If anyone later asks to cancel or "
                        + "move it, use handoff_to_human — you cannot change it from this chat."),
                    Booked: true);
            }

            case ClinicCrmOutcome.Taken:
                return new BotToolResult(await TakenMessageAsync(startUtc, startLocal, result.FreeTimes, ct));

            case ClinicCrmOutcome.Unavailable or ClinicCrmOutcome.NotAuthorised when other is not null:
                // The retry job pushes with THIS chat's number, so queuing would book them under the wrong person.
                return await PassBookingToClinicAsync(type, startLocal, other, ct);

            case ClinicCrmOutcome.Unavailable or ClinicCrmOutcome.NotAuthorised:
                return await QueueForClinicAsync(request, type, startLocal, customerName, result.Message, ct);

            default:
                // 400 (closed day, outside hours, past time…) and a 404 that survived the retry.
                return new BotToolResult(
                    $"Not booked: {result.Message ?? "the clinic's system refused that time."} Apologise briefly, "
                    + "then use check_availability again and offer a different time.");
        }
    }

    /// <summary>Our appointment note's limit. The CRM allows 2000; ours is the smaller one.</summary>
    private const int MaxNotesLength = 1000;

    /// <summary>The chat summary first, then the bot's own note if it adds anything.</summary>
    private static string? CombineNotes(string? summary, string? botNote)
    {
        var note = botNote?.Trim();
        var combined = summary switch
        {
            null => string.IsNullOrEmpty(note) ? null : note,
            _ when string.IsNullOrEmpty(note) || summary.Contains(note, StringComparison.OrdinalIgnoreCase) => summary,
            _ => $"{summary}\nNote: {note}",
        };

        return combined is { Length: > MaxNotesLength } ? combined[..MaxNotesLength] : combined;
    }

    // ── booking for someone other than the person chatting ────────────────────

    /// <summary>
    /// Another person a booking is for. The clinic's CRM knows people by WhatsApp number, so a name other
    /// than the chatter's only exists there under that person's own number.
    /// </summary>
    private sealed record OtherPerson(string Name, string Phone);

    /// <summary>Starts the note of a booking made for someone else — how we recognise one later.</summary>
    private const string BookedForPrefix = "BOOKED FOR ";

    private async Task<(OtherPerson? Person, string? Error)> ReadOtherPersonAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var name = ReadString(input, "for_name")?.Trim();
        var phoneText = ReadString(input, "for_whatsapp");

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phoneText)) return (null, null);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phoneText))
            return (null, "Cannot book for someone else without BOTH their name and their own WhatsApp number "
                + "(with country code). Ask the customer for whichever is missing, then try again.");

        var digits = new string(phoneText.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];

        if (digits.Length is < 8 or > 15)
            return (null, $"\"{phoneText}\" does not look like a WhatsApp number with the country code. "
                + "Ask the customer for it again.");

        var chatter = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        if (chatter is not null && chatter.Phone == digits)
            return (null, "That is the number of the person you are chatting with. A booking under this number "
                + $"always carries the name on file; it cannot be put under \"{name}\". Do not book it. Tell the "
                + "customer, and ask for the other person's own WhatsApp number.");

        return (new OtherPerson(name, digits), null);
    }

    private static string BookedForLabel(OtherPerson other, Contact? chatter, string chatterName)
    {
        var by = string.IsNullOrWhiteSpace(chatter?.Name) ? chatterName : chatter!.Name;
        var byPhone = chatter is null ? "" : $" (+{chatter.Phone})";
        return $"{BookedForPrefix}{other.Name} (+{other.Phone}) — requested in WhatsApp chat by {by}{byPhone}.";
    }

    private static string LimitNotes(string notes) =>
        notes.Length > MaxNotesLength ? notes[..MaxNotesLength] : notes;

    private static bool IsForSomeoneElse(Appointment? appointment) =>
        appointment?.Notes?.StartsWith(BookedForPrefix, StringComparison.Ordinal) == true;

    private const string ForSomeoneElseReply =
        "That booking was made for another person under their own number, so it cannot be cancelled or moved "
        + "from this chat. Use handoff_to_human so the clinic's team can do it.";

    /// <summary>
    /// "Taken" is usually someone else. But the clinic holds one booking at a time, so a customer asking for a
    /// time that clashes with THEIR OWN booking — a second person at the same hour — gets the same refusal, and
    /// "just taken" then sends them off confused about a slot they are already holding.
    /// </summary>
    private async Task<string> TakenMessageAsync(
        DateTime startUtc, DateTime startLocal, IReadOnlyList<string>? freeTimes, CancellationToken ct)
    {
        var own = await db.Appointments.AsNoTracking()
            .Include(a => a.AppointmentType)
            .Where(a => a.ContactId == scope.ContactId
                     && a.Status != AppointmentStatus.Cancelled
                     && (a.StartUtc == startUtc
                         || (a.StartUtc < startUtc && a.EndUtc > startUtc)))
            .FirstOrDefaultAsync(ct);

        if (own is null)
            return $"Not booked: {Describe(startLocal)} was just taken at the clinic. Apologise briefly. "
                   + OtherClinicTimes(startUtc, freeTimes);

        var ownLocal = TimeZoneInfo.ConvertTimeFromUtc(own.StartUtc, scope.TimeZone);

        return $"Not booked: {Describe(startLocal)} clashes with a booking this customer already holds "
               + $"({own.AppointmentType.Name} on {Describe(ownLocal)}). The clinic takes one booking at a time, so "
               + "the same time cannot be given to a second person. Do NOT say someone else took it. Tell them "
               + "plainly that they already have that time, and offer another. " + OtherClinicTimes(startUtc, freeTimes);
    }

    /// <summary>
    /// The clinic cannot be reached and the booking is for someone else. Queuing it would book under THIS
    /// chat's number, so a person takes it instead.
    /// </summary>
    private async Task<BotToolResult> PassBookingToClinicAsync(
        AppointmentType type, DateTime startLocal, OtherPerson other, CancellationToken ct)
    {
        const string acknowledgement =
            "NOT booked: the clinic's system could not be reached, and this booking is for another person, so "
            + "it has been passed to the team to make. Tell the customer in one short line that the team will "
            + "confirm it shortly. Do NOT say it is booked or confirmed.";

        await PauseForHumanAsync(ct);

        var chatter = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        var who = string.IsNullOrWhiteSpace(chatter?.Name) ? "A customer" : chatter!.Name;

        var detail = $"{who} (+{chatter?.Phone}) wants {type.Name} on {Describe(startLocal)} for {other.Name} "
                     + $"(+{other.Phone}). The clinic's system could not be reached, so please book it in the CRM "
                     + "under that number and confirm with them. The assistant is paused on this chat.";

        if (crm is not null) await crm.NotifyStaffAsync(scope.CompanyId, "Booking for someone else", detail, ct);

        if (staffAlerts is not null)
            await staffAlerts.AlertAsync(new StaffAlert(
                scope.CompanyId, scope.ContactId, scope.ConversationId,
                $"Wants {type.Name} on {Describe(startLocal)} for {other.Name} (+{other.Phone}). Please book it.",
                InApp: false), ct);

        logger.LogWarning(
            "Clinic CRM unreachable; booking for another person passed to staff on conversation {ConversationId}",
            scope.ConversationId);

        return new BotToolResult(acknowledgement, HandedOff: true);
    }

    /// <summary>
    /// The CRM is down: hold the slot here as Pending, let the sync job push it, and have the bot say the
    /// clinic will confirm — the API document's own advice for a 500. Never "booked".
    /// </summary>
    private async Task<BotToolResult> QueueForClinicAsync(
        CreateAppointmentRequest request, AppointmentType type, DateTime startLocal, string customerName,
        string? error, CancellationToken ct)
    {
        AppointmentResponse pending;
        try
        {
            pending = await appointments.CreateMirrorAsync(request, endUtc: null,
                new ExternalBookingLink(ExternalSyncStatus.Pending, Error: error), ct);
        }
        catch (AppException ex)
        {
            return new BotToolResult(
                $"Not booked: {ex.Message} Apologise briefly, then use check_availability again and offer a "
                + "different time.");
        }

        crm!.QueueSync(pending.Id);
        await BackfillContactNameAsync(customerName, ct);

        logger.LogWarning(
            "Clinic CRM unreachable; appointment {AppointmentId} queued for push on conversation {ConversationId}",
            pending.Id, request.ConversationId);

        return new BotToolResult(
            $"NOT confirmed yet: the clinic's booking system could not be reached, so the request for {type.Name} "
            + $"on {Describe(startLocal)} has been passed to the clinic, who will confirm it. A message saying the "
            + "request was received is sent automatically after your reply — do not repeat the details. Reply with "
            + "one short, warm line saying the clinic will confirm shortly. Do NOT say it is booked or confirmed.",
            Booked: true);
    }

    /// <summary>
    /// Records the name the customer gave, but never overwrites one already on file — the WhatsApp
    /// profile name and anything an agent typed are better sources than a name parsed out of chat.
    /// </summary>
    private async Task BackfillContactNameAsync(string customerName, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        if (contact is null || !string.IsNullOrWhiteSpace(contact.Name)) return;

        contact.Name = customerName.Trim();
        await db.SaveChangesAsync(ct);
    }

    // ── find_appointments / cancel_appointment / reschedule_appointment ───────

    /// <summary>
    /// The customer's own upcoming bookings. Scoped to <see cref="BotToolScope.ContactId"/> as well as
    /// the tenant filter, so the bot can only ever see and touch the appointments of the person it is
    /// actually talking to.
    /// </summary>
    private async Task<BotToolResult> FindAppointmentsAsync(CancellationToken ct)
    {
        // The same read the prompt's THEIR BOOKINGS section uses, so the two can never disagree.
        var bookings = await BotAppointmentHistory.LoadAsync(db, scope.ContactId, scope.TimeZone, ct);
        var upcoming = bookings.Where(b => b.IsUpcoming).ToList();
        var earlier = bookings.Where(b => !b.IsUpcoming).ToList();

        var sb = new StringBuilder();

        if (await ClinicEndpointAsync(ct) is not null)
        {
            // Connected: the clinic's own list, so bookings made by phone are seen and can be acted on.
            var (merged, unreachable) = await UpcomingBookingsAsync(ct);
            AppendUpcoming(sb, merged, unreachable);
        }
        else if (upcoming.Count == 0)
        {
            sb.AppendLine("They have no upcoming appointments. Say so plainly — do not offer to cancel or move "
                + "something that does not exist. Offer to book them in if that is what they want.");
        }
        else
        {
            sb.AppendLine("Their upcoming appointments:");
            foreach (var b in upcoming)
            {
                sb.Append("- ").Append(b.ServiceName).Append(" on ").Append(Describe(b.StartLocal))
                  .Append("  (start_local=").Append(b.StartLocal.ToString("yyyy-MM-dd'T'HH:mm", Invariant))
                  .AppendLine(")");
            }
        }

        if (earlier.Count > 0)
        {
            sb.AppendLine("Their most recent earlier bookings (already gone by or cancelled — these cannot be "
                + "changed; mention them only if relevant, e.g. when they last came in):");
            foreach (var b in earlier)
            {
                sb.Append("- ").Append(b.ServiceName).Append(" on ").Append(Describe(b.StartLocal))
                  .Append(" (").Append(BotAppointmentHistory.Outcome(b)).AppendLine(")");
            }
        }

        return new BotToolResult(sb.ToString().TrimEnd());
    }

    private async Task<BotToolResult> CancelAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var match = await MatchAsync(ReadString(input, "start_local"), ct);
        if (match.Error is not null) return new BotToolResult(match.Error);

        var target = match.Booking!;
        if (IsForSomeoneElse(target.Local)) return new BotToolResult(ForSomeoneElseReply);

        var local = TimeZoneInfo.ConvertTimeFromUtc(target.StartUtc, scope.TimeZone);

        // Made directly with the clinic (e.g. by phone): it exists only in the CRM.
        if (target.Local is not { } appointment)
            return await CancelClinicOnlyAsync(target, local, ReadString(input, "reason"), ct);

        if (appointment.ExternalSyncStatus == ExternalSyncStatus.Synced)
            return await CancelAtClinicAsync(appointment, local, ReadString(input, "reason"), ct);

        if (scope.DryRun)
            return new BotToolResult(
                $"[preview] Would cancel the appointment on {Describe(local)}. Nothing was actually "
                + "cancelled. Confirm it to the customer as if it succeeded.");

        try
        {
            await appointments.CancelAsync(
                appointment.Id, new CancelAppointmentRequest(ReadString(input, "reason")), ct);

            logger.LogInformation(
                "Bot cancelled appointment {AppointmentId} on conversation {ConversationId}",
                appointment.Id, scope.ConversationId);

            return new BotToolResult(
                $"Cancelled. The appointment on {Describe(local)} is gone. Confirm that to the customer "
                + "in one short line, and do not offer to rebook unless they ask.");
        }
        catch (AppException ex)
        {
            return new BotToolResult($"Not cancelled: {ex.Message}");
        }
    }

    private async Task<BotToolResult> RescheduleAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var match = await MatchAsync(ReadString(input, "from_start_local"), ct);
        if (match.Error is not null) return new BotToolResult(match.Error);

        var target = match.Booking!;
        if (IsForSomeoneElse(target.Local)) return new BotToolResult(ForSomeoneElseReply);

        var toText = ReadString(input, "to_start_local");
        if (toText is null || !TryParseLocal(toText, out var toLocal))
            return new BotToolResult(
                $"\"{toText}\" is not a time I can read. Use exactly YYYY-MM-DDTHH:MM.");

        if (WeekdayMismatch(ReadString(input, "to_weekday"), toLocal) is { } wrongDay)
            return new BotToolResult(wrongDay);

        if (CustomerNamedOtherDay(toLocal) is { } otherDay)
            return new BotToolResult(otherDay);

        DateTime toUtc;
        try
        {
            toUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal, scope.TimeZone);
        }
        catch (ArgumentException)
        {
            return new BotToolResult(
                "That local time does not exist on that date because the clocks change. Offer another.");
        }

        if (target.Local is not { } appointment
            || appointment.ExternalSyncStatus is ExternalSyncStatus.Synced or ExternalSyncStatus.Pending)
            return await MoveAtClinicAsync(target, toUtc, toLocal, ct);

        // AppointmentService.RescheduleAsync does NOT check offerability — it is built for staff, who
        // are trusted to make an after-hours exception deliberately, and it leans on the database's
        // overlap constraint alone. A bot moving a patient must be held to the same standard as a bot
        // BOOKING one, so the offerability gate is applied here rather than inherited.
        if (!await availability.IsOfferableAsync(appointment.AppointmentTypeId, toUtc, ct))
            return new BotToolResult(
                $"{Describe(toLocal)} is not a time we can offer. Use check_availability and offer them "
                + "a slot that is actually free.");

        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(appointment.StartUtc, scope.TimeZone);

        if (scope.DryRun)
            return new BotToolResult(
                $"[preview] Would move the appointment from {Describe(fromLocal)} to {Describe(toLocal)}. "
                + "Nothing was actually moved. Confirm it to the customer as if it succeeded.");

        try
        {
            await appointments.RescheduleAsync(
                appointment.Id, new RescheduleAppointmentRequest(toUtc), ct);

            logger.LogInformation(
                "Bot rescheduled appointment {AppointmentId} on conversation {ConversationId}",
                appointment.Id, scope.ConversationId);

            return new BotToolResult(
                $"Moved. It is now {Describe(toLocal)}, was {Describe(fromLocal)}. Confirm the new time "
                + "back to the customer in one short message.");
        }
        catch (AppException ex)
        {
            // SLOT_TAKEN from the overlap constraint lands here — someone else won the race.
            return new BotToolResult(
                $"Not moved: {ex.Message} Apologise briefly and offer a different time.");
        }
    }

    // ── cancel / reschedule for bookings the clinic's CRM holds ───────────────

    /// <summary>
    /// The clinic's API has no cancel, so the appointment service flags the cancellation on the CRM booking
    /// and cancels our copy, and staff are told to finish it in the CRM. If the CRM cannot be reached nothing
    /// is cancelled anywhere, and a person takes the request instead.
    /// </summary>
    private async Task<BotToolResult> CancelAtClinicAsync(
        Appointment appointment, DateTime local, string? reason, CancellationToken ct)
    {
        if (scope.DryRun)
            return new BotToolResult(
                $"[preview] Would cancel the appointment on {Describe(local)} and send the cancellation to the "
                + "clinic. Nothing was actually cancelled. Confirm it to the customer as if it succeeded.");

        AppointmentResponse cancelled;
        try
        {
            cancelled = await appointments.CancelAsync(appointment.Id, new CancelAppointmentRequest(reason), ct);
        }
        catch (AppException ex)
        {
            logger.LogWarning(
                "Could not send a cancellation of clinic booking {BookingId} to the CRM: {Error}",
                appointment.ExternalBookingId, ex.Message);
            return await PassCancellationToClinicAsync(
                appointment.AppointmentType.Name, appointment.ExternalBookingId, local, reason, ct);
        }

        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        var who = contact is null || string.IsNullOrWhiteSpace(contact.Name) ? "A customer" : contact.Name;
        var phone = contact is null ? "" : $" (+{contact.Phone})";

        // Cancelled in the CRM as well: nothing left for anyone to do. Otherwise a person must finish it.
        var cancelledAtClinic = cancelled.ExternalSyncStatus == ExternalSyncStatus.Synced
                                && cancelled.ExternalSyncError is null;

        if (crm is not null && !cancelledAtClinic)
            await crm.NotifyStaffAsync(scope.CompanyId, "Cancellation — finish it in the clinic CRM",
                $"{who}{phone} cancelled {appointment.AppointmentType.Name} on {Describe(local)} through the chat"
                + (string.IsNullOrWhiteSpace(reason) ? "." : $" (reason: {reason}).")
                + (cancelled.ExternalSyncStatus == ExternalSyncStatus.CancelRequested
                    ? $" Clinic booking #{appointment.ExternalBookingId} is flagged \"CANCELLATION REQUESTED\" — "
                      + "please cancel it in the CRM; the clinic's system would not cancel it."
                    : $" {cancelled.ExternalSyncError}"), ct);

        logger.LogInformation(
            "Bot cancelled appointment {AppointmentId} (clinic booking {BookingId}, {SyncStatus}) on conversation {ConversationId}",
            appointment.Id, appointment.ExternalBookingId, cancelled.ExternalSyncStatus, scope.ConversationId);

        return new BotToolResult(
            $"Cancelled. The appointment on {Describe(local)} is cancelled and the clinic has been told. Confirm "
            + "that to the customer in one short line, and do not offer to rebook unless they ask.");
    }

    /// <summary>The fallback when the clinic's system cannot take the cancellation: a person handles it.</summary>
    private async Task<BotToolResult> PassCancellationToClinicAsync(
        string serviceName, int? bookingId, DateTime local, string? reason, CancellationToken ct)
    {
        const string acknowledgement =
            "Passed on — the clinic's team cancels bookings themselves, and a person will confirm it with the "
            + "customer. Tell the customer in one short line that their cancellation request has been passed to "
            + "the team, who will confirm it shortly. Say nothing else.";

        if (scope.DryRun)
            return new BotToolResult("[preview] " + acknowledgement, HandedOff: true);

        await PauseForHumanAsync(ct);

        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        var who = contact is null ? "A customer" : $"{(string.IsNullOrWhiteSpace(contact.Name) ? "A customer" : contact.Name)} (+{contact.Phone})";

        if (crm is not null)
            await crm.NotifyStaffAsync(scope.CompanyId, "Cancellation request",
                $"{who} wants to cancel {serviceName} on {Describe(local)} "
                + $"(clinic booking #{bookingId})."
                + (string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}.")
                + " The assistant cannot cancel clinic bookings — please cancel it in the CRM and confirm with them. "
                + "The assistant is paused on this chat.", ct);

        if (staffAlerts is not null)
            await staffAlerts.AlertAsync(new StaffAlert(
                scope.CompanyId, scope.ContactId, scope.ConversationId,
                $"Wants to cancel {serviceName} on {Describe(local)}. Please cancel it in the CRM and confirm.",
                InApp: false), ct);

        logger.LogInformation(
            "Bot passed a cancellation of clinic booking {BookingId} to staff on conversation {ConversationId}",
            bookingId, scope.ConversationId);

        return new BotToolResult(acknowledgement, HandedOff: true);
    }

    /// <summary>
    /// Moves a CRM booking through the CRM, which applies its own rules and answers with a NEW booking id.
    /// A booking still waiting to reach the CRM cannot be moved safely — two pushes could race — so a person
    /// takes it.
    /// </summary>
    private async Task<BotToolResult> MoveAtClinicAsync(
        UpcomingBooking target, DateTime toUtc, DateTime toLocal, CancellationToken ct)
    {
        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(target.StartUtc, scope.TimeZone);

        var endpoint = await ClinicEndpointAsync(ct);
        if (target.ClinicBookingId is not { } bookingId
            || target.Local is { ExternalSyncStatus: not ExternalSyncStatus.Synced }
            || endpoint is null)
        {
            return new BotToolResult(
                "This booking cannot be moved through the chat right now. Use handoff_to_human so someone from "
                + "the clinic can change it for them.");
        }

        if (scope.DryRun)
            return new BotToolResult(
                $"[preview] Would move the appointment from {Describe(fromLocal)} to {Describe(toLocal)} at the "
                + "clinic. Nothing was actually moved. Confirm it to the customer as if it succeeded.");

        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        if (contact is null) return new BotToolResult("This booking cannot be moved from this conversation.");

        var result = await crm!.MoveAsync(endpoint, bookingId, contact.Phone, toUtc, ct);

        switch (result.Outcome)
        {
            case ClinicCrmOutcome.Ok when result.Data is { } change:
            {
                var booking = change.Booking;
                var endUtc = ClinicCrmFormat.ToUtc(booking.Date, booking.EndTime, crm.CrmTimeZone);

                try
                {
                    // A phone booking has no copy here to move — the clinic's CRM is all there is.
                    if (target.Local is { } appointment)
                        await appointments.ApplyExternalMoveAsync(appointment.Id, toUtc, endUtc, booking.BookingId, ct);
                }
                catch (AppException ex)
                {
                    logger.LogError(
                        "Clinic CRM moved booking {BookingId} but appointment {AppointmentId} could not follow: {Error}",
                        booking.BookingId, target.Local?.Id, ex.Message);

                    await crm.NotifyStaffAsync(scope.CompanyId, "Clinic booking moved, calendar not updated",
                        $"The clinic CRM moved booking #{bookingId} to {Describe(toLocal)} (now #{booking.BookingId}), "
                        + "but our calendar has something at that time, so it still shows the old time. Please check it.",
                        ct);
                }

                logger.LogInformation(
                    "Bot moved clinic booking {OldBookingId} to {NewBookingId} on conversation {ConversationId}",
                    bookingId, booking.BookingId, scope.ConversationId);

                return new BotToolResult(
                    $"Moved. It is now {Describe(toLocal)}, was {Describe(fromLocal)}. Confirm the new time back to "
                    + "the customer in one short message.");
            }

            case ClinicCrmOutcome.Taken:
                return new BotToolResult(
                    $"Not moved: {result.Message ?? $"{Describe(toLocal)} is not free at the clinic."} Apologise briefly. "
                    + OtherClinicTimes(toUtc, result.FreeTimes));

            case ClinicCrmOutcome.Rejected:
                return new BotToolResult(
                    $"Not moved: {result.Message ?? "the clinic's system refused that time."} Apologise briefly and "
                    + "offer a different time.");

            default:
                // 404 (the clinic changed or removed it in the CRM) or the CRM is unreachable.
                return new BotToolResult(
                    "The booking could not be moved through the clinic's system just now. Apologise briefly and use "
                    + "handoff_to_human so someone from the clinic can change it for them.");
        }
    }

    /// <summary>
    /// The customer's upcoming, non-cancelled appointments, soonest first. Past bookings are excluded:
    /// nobody means yesterday when they say "my appointment".
    /// </summary>
    private async Task<List<Appointment>> UpcomingAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Include, not a manual Join: EF cannot translate a ValueTuple projection, and the navigation
        // is right there. (The join version compiled fine and blew up only against a real database.)
        return await db.Appointments.AsNoTracking()
            .Include(a => a.AppointmentType)
            .Where(a => a.ContactId == scope.ContactId
                     && a.Status != AppointmentStatus.Cancelled
                     && a.StartUtc >= now)
            .OrderBy(a => a.StartUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Resolves the appointment the model named by its start time. Returns an explanatory message
    /// instead of a match when it cannot — never a guess, because cancelling or moving the wrong
    /// booking is the one mistake here that a customer really notices.
    /// </summary>
    private async Task<(UpcomingBooking? Booking, string? Error)> MatchAsync(
        string? startLocalText, CancellationToken ct)
    {
        if (startLocalText is null)
            return (null, "I need the appointment's start_local. Call find_appointments first.");

        if (!TryParseLocal(startLocalText, out var local))
            return (null, $"\"{startLocalText}\" is not a time I can read. Use YYYY-MM-DDTHH:MM.");

        var (upcoming, unreachable) = await UpcomingBookingsAsync(ct);
        var caveat = unreachable
            ? " The clinic's system could not be reached, so bookings made directly with the clinic are not "
              + "shown — if they mean one of those, use handoff_to_human."
            : "";

        if (upcoming.Count == 0)
            return (null, "They have no upcoming appointments, so there is nothing to change." + caveat);

        // Compare in local time: that is the form the model was given and the form the customer used.
        var match = upcoming.FirstOrDefault(b =>
            TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, scope.TimeZone) == local);

        if (match is null)
        {
            var options = string.Join(", ", upcoming.Select(b =>
                Describe(TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, scope.TimeZone))));

            return (null,
                $"No appointment starts at {Describe(local)}. What they actually have: {options}. "
                + "Ask which one they mean." + caveat);
        }

        return (match, null);
    }

    /// <summary>
    /// One upcoming booking as the bot can act on it. <see cref="Local"/> is our copy (null for a booking made
    /// directly with the clinic); <see cref="ClinicBookingId"/> is the CRM's (null when the CRM does not hold
    /// it — still being sent, or booked before the connection).
    /// </summary>
    private sealed record UpcomingBooking(
        DateTime StartUtc, string ServiceName, Appointment? Local, int? ClinicBookingId, string? Origin);

    /// <summary>
    /// The customer's upcoming bookings. Without a CRM connection, ours. With one, the clinic's own list —
    /// the only place a booking made by phone appears — matched to our copies by booking id, plus ours the
    /// clinic does not hold yet. A copy the clinic no longer lists was cancelled or moved there, so it is
    /// not offered. If the clinic cannot be reached, ours alone, flagged as possibly incomplete.
    /// </summary>
    private async Task<(List<UpcomingBooking> Bookings, bool ClinicUnreachable)> UpcomingBookingsAsync(
        CancellationToken ct)
    {
        var local = await UpcomingAsync(ct);

        List<UpcomingBooking> Ours() =>
            [.. local.Select(a => new UpcomingBooking(
                a.StartUtc, a.AppointmentType.Name, a, a.ExternalBookingId,
                a.ExternalSyncStatus == ExternalSyncStatus.Pending ? "waiting for the clinic to confirm" : null))];

        if (await ClinicEndpointAsync(ct) is not { } endpoint) return (Ours(), false);

        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        if (contact is null) return (Ours(), false);

        var clinic = await crm!.GetUpcomingAsync(endpoint, contact.Phone, ct);
        if (!clinic.IsOk) return (Ours(), true);

        var now = DateTime.UtcNow;
        var merged = new List<UpcomingBooking>();

        foreach (var booking in clinic.Data ?? [])
        {
            if (ClinicCrmFormat.ToUtc(booking.Date, booking.StartTime, crm.CrmTimeZone) is not { } startUtc
                || startUtc < now)
                continue;

            var mirror = local.FirstOrDefault(a => a.ExternalBookingId == booking.BookingId);
            var treatments = booking.Treatments is { Count: > 0 } t ? string.Join(", ", t.Select(x => x.Name)) : null;

            merged.Add(new UpcomingBooking(
                startUtc,
                treatments ?? mirror?.AppointmentType.Name ?? "Appointment",
                mirror,
                booking.BookingId,
                mirror is null ? "booked directly with the clinic, e.g. by phone" : "booked through this chat"));
        }

        merged.AddRange(Ours().Where(b => b.Local!.ExternalSyncStatus != ExternalSyncStatus.Synced));

        return ([.. merged.OrderBy(b => b.StartUtc)], false);
    }

    private void AppendUpcoming(StringBuilder sb, List<UpcomingBooking> upcoming, bool clinicUnreachable)
    {
        if (upcoming.Count == 0)
        {
            sb.AppendLine("They have no upcoming appointments. Say so plainly — do not offer to cancel or move "
                + "something that does not exist. Offer to book them in if that is what they want.");
        }
        else
        {
            sb.AppendLine("Their upcoming appointments (all of them, including any made by phone):");
            foreach (var b in upcoming)
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, scope.TimeZone);
                sb.Append("- ").Append(b.ServiceName).Append(" on ").Append(Describe(local));
                if (b.Origin is not null) sb.Append(" — ").Append(b.Origin);
                sb.Append("  (start_local=").Append(local.ToString("yyyy-MM-dd'T'HH:mm", Invariant)).AppendLine(")");
            }

            if (upcoming.Count > 1)
                sb.AppendLine("If they want to cancel or move more than one, confirm which, then call the tool once "
                    + "for each, all in the same turn.");
        }

        if (clinicUnreachable)
            sb.AppendLine("The clinic's system could not be reached, so bookings made directly with the clinic "
                + "(e.g. by phone) may be missing from this list. If they mention one that is not here, offer to "
                + "have someone from the clinic help them (handoff_to_human).");
    }

    /// <summary>
    /// A booking made directly with the clinic: the CRM is all there is, and its API cannot cancel, so the
    /// booking is flagged for the clinic to cancel and staff are told. If the CRM cannot be reached, a person
    /// takes it.
    /// </summary>
    private async Task<BotToolResult> CancelClinicOnlyAsync(
        UpcomingBooking target, DateTime local, string? reason, CancellationToken ct)
    {
        if (scope.DryRun)
            return new BotToolResult(
                $"[preview] Would send the cancellation of the appointment on {Describe(local)} to the clinic. "
                + "Nothing was actually cancelled. Confirm it to the customer as if it succeeded.");

        var endpoint = await ClinicEndpointAsync(ct);
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.ContactId, ct);
        if (endpoint is null || contact is null || target.ClinicBookingId is not { } bookingId)
            return await PassCancellationToClinicAsync(target.ServiceName, target.ClinicBookingId, local, reason, ct);

        var result = await crm!.CancelBookingAsync(endpoint, bookingId, contact.Phone, reason, ct);

        switch (result.Outcome)
        {
            case ClinicCrmCancelOutcome.Cancelled:
                logger.LogInformation(
                    "Bot cancelled clinic-only booking {BookingId} on conversation {ConversationId}",
                    bookingId, scope.ConversationId);

                return new BotToolResult(
                    $"Cancelled. The appointment on {Describe(local)} is cancelled at the clinic. Confirm that to the "
                    + "customer in one short line, and do not offer to rebook unless they ask.");

            case ClinicCrmCancelOutcome.Flagged:
                var who = string.IsNullOrWhiteSpace(contact.Name) ? "A customer" : contact.Name;
                await crm.NotifyStaffAsync(scope.CompanyId, "Cancellation — finish it in the clinic CRM",
                    $"{who} (+{contact.Phone}) cancelled {target.ServiceName} on {Describe(local)} through the chat"
                    + (string.IsNullOrWhiteSpace(reason) ? "." : $" (reason: {reason}).")
                    + $" It was booked directly with the clinic. Clinic booking #{bookingId} is flagged "
                    + "\"CANCELLATION REQUESTED\" — please cancel it in the CRM; the clinic's system would not cancel it.",
                    ct);

                logger.LogInformation(
                    "Bot flagged clinic-only booking {BookingId} for cancellation on conversation {ConversationId}",
                    bookingId, scope.ConversationId);

                return new BotToolResult(
                    $"Cancelled. The appointment on {Describe(local)} is cancelled and the clinic has been told. "
                    + "Confirm that to the customer in one short line, and do not offer to rebook unless they ask.");

            case ClinicCrmCancelOutcome.NotFound:
                return new BotToolResult(
                    $"The appointment on {Describe(local)} is no longer booked at the clinic — it may already have "
                    + "been cancelled. Tell the customer it no longer shows as booked.");

            default:
                return await PassCancellationToClinicAsync(target.ServiceName, bookingId, local, reason, ct);
        }
    }

    // ── handoff_to_human ──────────────────────────────────────────────────────

    private async Task<BotToolResult> HandoffAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        const string acknowledgement =
            "Done — a person will pick this up. Send one short message telling the customer someone "
            + "will get back to them shortly, and say nothing else.";

        if (scope.DryRun) return new BotToolResult(acknowledgement, HandedOff: true);

        await PauseForHumanAsync(ct);

        // The team learns about it only if someone tells them: the bot goes quiet on this chat, and nobody
        // is watching a paused thread.
        if (staffAlerts is not null)
            await staffAlerts.AlertAsync(new StaffAlert(
                scope.CompanyId, scope.ContactId, scope.ConversationId,
                await HandoffSummaryAsync(ReadString(input, "reason"), ct)), ct);

        return new BotToolResult(acknowledgement, HandedOff: true);
    }

    /// <summary>
    /// What the team reads: the model's own one-line reason, plus the customer's last message, because
    /// "asked something I could not answer" means little without the question.
    /// </summary>
    private async Task<string> HandoffSummaryAsync(string? reason, CancellationToken ct)
    {
        var last = scope.ConversationId is { } conversationId
            ? await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && !m.IsDeleted
                         && m.Direction == MessageDirection.Inbound && m.Body != null)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Body)
                .FirstOrDefaultAsync(ct)
            : null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(reason)) parts.Add(reason.Trim().TrimEnd('.'));
        if (!string.IsNullOrWhiteSpace(last))
        {
            var quoted = last.Trim();
            parts.Add($"Their last message: \"{(quoted.Length <= 120 ? quoted : quoted[..120] + "…")}\"");
        }

        return string.Join(". ", parts);
    }

    private async Task PauseForHumanAsync(CancellationToken ct)
    {
        if (scope.DryRun || scope.ConversationId is not { } conversationId) return;

        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null) return;

        conversation.BotState = ConversationBotState.Paused;
        conversation.BotPausedAt = DateTime.UtcNow;
        conversation.BotPausedByUserId = null;      // the bot paused itself; no agent owns it yet

        // Open-ended unless the business asked for a safety net: an escalation should not un-escalate on
        // a short timer by default, but a handoff nobody picks up should not silence a customer for a day.
        var resumeMinutes = await db.BotProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.CompanyId == scope.CompanyId)
            .Select(p => p.HandoffResumeMinutes)
            .FirstOrDefaultAsync(ct);
        conversation.BotResumesAt = resumeMinutes > 0 ? DateTime.UtcNow.AddMinutes(resumeMinutes) : null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Bot handed conversation {ConversationId} to a human", conversationId);
    }

    // ── share_link ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the title the model asked for. Nothing is sent here — the id rides back on the result
    /// and the job sends the stored URL after the reply, so the model can never put a URL of its own
    /// making in front of a customer. Same in preview: the preview service renders the bubble.
    /// </summary>
    private async Task<BotToolResult> ShareLinkAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var links = await BotLinkMessages.Enabled(db).ToListAsync(ct);
        if (links.Count == 0)
            return new BotToolResult(
                "This business has no links to share. Do not promise to send anything.");

        var requested = ReadString(input, "link_title");
        var match = requested is null ? null
            : links.FirstOrDefault(l => string.Equals(l.Title, requested, StringComparison.OrdinalIgnoreCase))
              ?? (links.Where(l => l.Title.Contains(requested, StringComparison.OrdinalIgnoreCase)
                                || requested.Contains(l.Title, StringComparison.OrdinalIgnoreCase))
                       .ToList() is [var only] ? only : null);

        if (match is null)
        {
            var titles = string.Join(", ", links.Select(l => $"\"{l.Title}\""));
            return new BotToolResult(
                $"There is no link called \"{requested}\". The links are: {titles}. Pick one of those, "
                + "or if none fits, do not promise to send anything.");
        }

        // Once per conversation. The transcript shows an earlier link only as plain bot text, so nothing
        // else stops the model re-sending it — and it did, twice, to a customer who had already said no
        // and was asking for the address.
        if (scope.ConversationId is { } conversationId
            && await db.Messages.AsNoTracking().AnyAsync(
                m => m.ConversationId == conversationId
                  && m.Direction == MessageDirection.Outbound
                  && m.Body.Contains(match.Url), ct))
        {
            return new BotToolResult(
                $"\"{match.Title}\" was already sent earlier in this chat, so it will NOT be sent again. "
                + "Do not offer it or mention it again. Answer what they actually asked.");
        }

        return new BotToolResult(
            $"Done — \"{match.Title}\" is sent as its own message right after your reply. Do not paste a "
            + "URL. Your reply must still answer what they asked first; the link just follows it.",
            SharedLinkId: match.Id);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The services this bot may book: every enabled type (switched on and off in Calendar settings).
    /// Tenant scoping comes from the global query filter, which the job has entered.
    /// </summary>
    private async Task<IReadOnlyList<AppointmentType>> BookableTypesAsync(CancellationToken ct)
    {
        return await db.AppointmentTypes.AsNoTracking()
            .Where(t => t.IsActive && t.IsEnabled)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Matches a name the model produced against the real service list: exact first, then
    /// case-insensitive, then a unique substring. An ambiguous or unknown name returns null so the
    /// caller can hand the model the real options instead of it picking one at random.
    /// </summary>
    private static AppointmentType? ResolveType(IReadOnlyList<AppointmentType> types, string? requested)
    {
        if (types.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(requested))
            return types.Count == 1 ? types[0] : null;

        var name = requested.Trim();

        var exact = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var partial = types
            .Where(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                     || name.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return partial.Count == 1 ? partial[0] : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static DateOnly? ReadDate(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        var text = ReadString(input, key);
        if (text is null) return null;

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>
    /// The optional <c>time</c> argument. An unreadable one is ignored rather than refused: it only adds a
    /// verdict line, and the slot list is still there without it.
    /// </summary>
    private static TimeOnly? ReadTime(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        var text = ReadString(input, key);
        if (text is null) return null;

        return TimeOnly.TryParseExact(text, ["HH:mm", "H:mm", "HH:mm:ss"], Invariant, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    /// <summary>
    /// Parses the wall-clock time the model was told to send. Kind stays Unspecified: it is a local
    /// time in the company's zone, and letting it become Local would silently apply the SERVER's offset.
    /// </summary>
    private static bool TryParseLocal(string text, out DateTime local)
    {
        string[] formats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm"];

        return DateTime.TryParseExact(
            text, formats, Invariant, DateTimeStyles.NoCurrentDateDefault, out local);
    }

    /// <summary>
    /// Catches the model agreeing "Sunday" with the customer but sending another day's date — the one
    /// date mistake a customer only discovers by turning up on the wrong day. Null when they agree, or
    /// when no weekday was given.
    /// </summary>
    private static string? WeekdayMismatch(string? weekday, DateTime local)
    {
        if (weekday is null || weekday.Length < 3) return null;

        var actual = local.DayOfWeek.ToString();
        if (actual.StartsWith(weekday[..3], StringComparison.OrdinalIgnoreCase)) return null;

        return $"Nothing was done: {local:yyyy-MM-dd} is a {actual}, not a {weekday}. Work out the correct "
               + $"date for the {weekday} the customer agreed to (use the list of upcoming dates you were "
               + "given) and call again with it. Do not tell the customer anything has been booked yet.";
    }

    private static readonly Regex DayNames = new(
        @"\b(?<d>monday|mon|tuesday|tues|tue|wednesday|thursday|thurs|thur|thu|friday|fri|saturday|sunday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The model's own <c>weekday</c> argument only proves it agrees with itself — it booked "Monday 2 Nov"
    /// for a customer who had just written "thursday 2pm", with the weekday argument set to Monday. This
    /// checks the booking against the customer's own latest words instead. Null when that message names
    /// no weekday (a bare "yes" is fine), or when it names the booked one.
    /// </summary>
    private string? CustomerNamedOtherDay(DateTime local)
    {
        if (string.IsNullOrWhiteSpace(scope.CustomerText)) return null;

        var named = DayNames.Matches(scope.CustomerText)
            .Select(m => m.Groups["d"].Value[..3].ToLowerInvariant())
            .Distinct()
            .ToList();

        if (named.Count == 0) return null;

        var actual = local.DayOfWeek.ToString();
        if (named.Any(n => actual.StartsWith(n, StringComparison.OrdinalIgnoreCase))) return null;

        return $"Nothing was done: {local:yyyy-MM-dd} is a {actual}, but the customer's last message asked for "
               + $"a different day. Take the date for the day THEY named from the list of upcoming dates and "
               + "call check_availability for that exact date instead. Do not tell the customer anything is "
               + "free or booked yet.";
    }

    /// <summary>How a slot is written to the model, and roughly how a person would say it.</summary>
    private static string Describe(DateTime local) =>
        local.ToString("ddd d MMM, h:mm tt", Invariant);
}
