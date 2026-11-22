using System.Text.Json;
using Anthropic.Models.Messages;

namespace wa_api.Features.Bot.Ai.Tools;

/// <summary>
/// The three things the bot can actually DO, as tool definitions.
/// <para>
/// This is where the engine gets its power and its safety at the same time. The model decides WHEN to
/// look for times and WHEN to book; <c>IAvailabilityService.IsOfferableAsync</c> and the
/// <c>EX_Appointments_NoOverlap</c> exclusion constraint decide WHETHER it may. So a model that
/// hallucinates a slot does not create one — it gets a tool result telling it the slot is not
/// available, and has to recover in conversation like a person would.
/// </para>
/// <para>
/// The lists are deliberately fixed and ordered: tools render BEFORE the system prompt in the cached
/// prefix, so reordering one, or picking tools per message, would silently break prompt caching. There
/// are exactly two lists, <see cref="All"/> and <see cref="NoBooking"/>, chosen by the company's
/// <c>BookingsEnabled</c> setting. That choice is stable for a company, so each one caches as before.
/// </para>
/// <para>
/// <c>Strict</c> is deliberately NOT set. Strict mode requires <c>additionalProperties: false</c> in
/// the schema, which the C# <see cref="InputSchema"/> cannot express, and the executor has to validate
/// every argument anyway — a well-formed <c>service_name</c> can still name a service that does not
/// exist, and a well-formed date string can still be nonsense.
/// </para>
/// </summary>
public static class BotTools
{
    public const string CheckAvailability = "check_availability";
    public const string BookAppointment = "book_appointment";
    public const string FindAppointments = "find_appointments";
    public const string CancelAppointment = "cancel_appointment";
    public const string RescheduleAppointment = "reschedule_appointment";
    public const string HandoffToHuman = "handoff_to_human";
    public const string ShareLink = "share_link";

    /// <summary>The tool list as sent to the model, in a fixed order. Cached — see the type remarks.</summary>
    public static IReadOnlyList<ToolUnion> All => AllUnions;

    /// <summary>
    /// The tools for a business that does not take bookings: hand off and share a link. Same relative
    /// order as <see cref="All"/>.
    /// </summary>
    public static IReadOnlyList<ToolUnion> NoBooking => NoBookingUnions;

    /// <summary>The names in <see cref="NoBooking"/>.</summary>
    public static IReadOnlySet<string> NoBookingNames { get; } = new HashSet<string> { HandoffToHuman, ShareLink };

    /// <summary>The list to send for a company, by its bookings setting.</summary>
    public static IReadOnlyList<ToolUnion> For(bool bookingsEnabled) => bookingsEnabled ? All : NoBooking;

    /// <summary>The definitions to show on the settings screen for a company, by its bookings setting.</summary>
    public static IReadOnlyList<Tool> DefinitionsFor(bool bookingsEnabled) =>
        bookingsEnabled ? Definitions : [.. Definitions.Where(t => NoBookingNames.Contains(t.Name))];

    /// <summary>Whether a company with this bookings setting may run the named tool.</summary>
    public static bool IsAllowed(string toolName, bool bookingsEnabled) =>
        bookingsEnabled || NoBookingNames.Contains(toolName);

    /// <summary>The same tools as plain definitions, for showing on the settings screen.</summary>
    public static IReadOnlyList<Tool> Definitions { get; } =
    [
        new Tool
        {
            Name = CheckAvailability,
            Description =
                "Find real free appointment slots. Call this BEFORE offering the customer any specific "
                + "time. Returns up to a dozen slots in the business's local time. If you leave "
                + "service_name out and the business offers more than one service, you will get the "
                + "list of services back so you can ask which one they want. When the customer has "
                + "named a time, pass it as time: the reply then opens with a REQUESTED TIME line "
                + "saying outright whether that time is free.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["service_name"] = Str(
                        "The service to check, exactly as it is named in your list of services. "
                        + "Omit if the customer has not said which service they want yet."),
                    ["from_date"] = Str(
                        "First date to search, as YYYY-MM-DD in the business's local time. "
                        + "Omit for today."),
                    ["to_date"] = Str(
                        "Last date to search, as YYYY-MM-DD. Omit to search the next week."),
                    ["time"] = Str(
                        "The time the customer asked for, as HH:MM in 24-hour time (5 PM is 17:00). "
                        + "Set from_date and to_date to the day they asked about. Omit if they have "
                        + "not named a time."),
                },
                Required = [],
            },
        },
        new Tool
        {
            Name = BookAppointment,
            Description =
                "Make the booking. Only call this after you have sent the customer the details (service, "
                + "day, date, time and name) of a time from a check_availability result and they have said "
                + "yes to them. It refuses a booking the customer has not confirmed: if so, send the "
                + "details and ask them to confirm. If the reply says the slot was taken or is not "
                + "available, apologise briefly and offer them a different time.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["service_name"] = Str(
                        "The service being booked, exactly as it is named in your list of services."),
                    ["start_local"] = Str(
                        "The agreed start time in the business's local time, as YYYY-MM-DDTHH:MM "
                        + "(24-hour). It must be a slot that appeared in a check_availability result."),
                    ["weekday"] = Str(
                        "The day of the week you agreed with the customer for start_local, e.g. \"Sunday\". "
                        + "The booking is refused if start_local falls on a different day."),
                    ["customer_name"] = Str(
                        "The name of the person you are chatting with, as they gave it. Even when the "
                        + "booking is for someone else, this is still the chatter's own name."),
                    ["for_name"] = Str(
                        "Only when the booking is for ANOTHER person (a brother, a friend): that person's "
                        + "name. Always send for_whatsapp with it. Omit for the chatter's own booking."),
                    ["for_whatsapp"] = Str(
                        "Only with for_name: the other person's WhatsApp number with country code, which "
                        + "you must have asked for. A booking cannot be put under someone else's name "
                        + "without their own number."),
                    ["notes"] = Str(
                        "Anything the customer asked you to pass on. Omit if there is nothing."),
                },
                Required = ["service_name", "start_local", "weekday", "customer_name"],
            },
        },
        new Tool
        {
            Name = FindAppointments,
            Description =
                "List this customer's bookings: the ones coming up, plus their most recent past and "
                + "cancelled ones. Call this whenever they ask about their appointment, their visits or "
                + "when they are due in, and ALWAYS FIRST when they mention changing, moving or cancelling "
                + "a booking — you need to know what they actually have before you can act on it, and you "
                + "must never guess. Only upcoming bookings can be changed or cancelled.",
            InputSchema = new() { Properties = new Dictionary<string, JsonElement>(), Required = [] },
        },
        new Tool
        {
            Name = CancelAppointment,
            Description =
                "Cancel an appointment the customer already has. Only call this after find_appointments "
                + "has shown you the booking, and only once the customer has clearly confirmed they want "
                + "it cancelled. Cancelling is not easily undone, so never do it on a maybe.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["start_local"] = Str(
                        "The start time of the appointment to cancel, exactly as find_appointments "
                        + "gave it to you, as YYYY-MM-DDTHH:MM."),
                    ["reason"] = Str("Why they are cancelling, if they said. Omit if they did not."),
                },
                Required = ["start_local"],
            },
        },
        new Tool
        {
            Name = RescheduleAppointment,
            Description =
                "Move an existing appointment to a different time. Call find_appointments first to see "
                + "what they have, and check_availability to make sure the new time is actually free. "
                + "The new time must be one you saw in a check_availability result.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["from_start_local"] = Str(
                        "The current start time of the appointment, as YYYY-MM-DDTHH:MM."),
                    ["to_start_local"] = Str(
                        "The new start time they want, as YYYY-MM-DDTHH:MM."),
                    ["to_weekday"] = Str(
                        "The day of the week you agreed with the customer for to_start_local, e.g. \"Sunday\". "
                        + "The move is refused if to_start_local falls on a different day."),
                },
                Required = ["from_start_local", "to_start_local", "to_weekday"],
            },
        },
        new Tool
        {
            Name = HandoffToHuman,
            Description =
                "Hand this conversation to a member of staff and STOP replying. It is for things you "
                + "genuinely cannot do or answer — anything you do not know or are unsure of — such as refunds, billing, a question the "
                + "business details do not cover (a service or product that is not listed, comparing them), "
                + "a question the customer has had to repeat, or when the customer directly asks to "
                + "speak to a person. If you can cancel and move bookings yourself, never hand those off. "
                + "Do NOT call this because someone is annoyed, unhappy with the service, or says they "
                + "might go elsewhere — that is a conversation to have, not one to escape, and calling "
                + "it there ends the chat with nobody watching. "
                + "After calling it, send one short message telling the customer someone will get back.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["reason"] = Str("One short line telling the staff member what this is about."),
                },
                Required = ["reason"],
            },
        },
        // Appended last on purpose: tools sit at the front of the cached prefix, so adding one at the
        // end costs a single cache miss rather than reshuffling everything before it.
        new Tool
        {
            Name = ShareLink,
            Description =
                "Send the customer one of the business's links (photos, price list, map...). Pass the "
                + "link's title exactly as it appears under LINKS YOU CAN SHARE. The link is sent as its "
                + "own message right after your reply, so never type or paste a URL yourself. Only "
                + "links from that list exist — never promise to send anything that is not on it.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["link_title"] = Str("The title of the link to send, exactly as listed."),
                },
                Required = ["link_title"],
            },
        },
    ];

    private static readonly IReadOnlyList<ToolUnion> AllUnions = [.. Definitions.Select(t => (ToolUnion)t)];

    private static readonly IReadOnlyList<ToolUnion> NoBookingUnions =
        [.. Definitions.Where(t => NoBookingNames.Contains(t.Name)).Select(t => (ToolUnion)t)];

    private static JsonElement Str(string description) =>
        JsonSerializer.SerializeToElement(new { type = "string", description });
}
