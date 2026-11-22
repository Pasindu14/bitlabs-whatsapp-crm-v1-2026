using wa_api.Common.Entities;
using wa_api.Common.Tenancy;

namespace wa_api.Features.Bot.Entities;

/// <summary>
/// Everything the AI reply engine needs to know about a tenant's business that the rest of the
/// platform does not already record. Exactly one row per company.
/// <para>
/// What is deliberately NOT here: services, prices and durations (<c>AppointmentType</c>), opening
/// hours (<c>BusinessHours</c>), closures (<c>CalendarBlackout</c>) and the timezone
/// (<c>Company.TimeZoneId</c>). Those are the calendar module's source of truth and the bot reads them
/// from there — duplicating them would let the bot quote a price or an opening time that the booking
/// endpoint then refuses, which is the worst failure this engine can have.
/// </para>
/// </summary>
public class BotProfile : BaseEntity, ITenantEntity
{
    /// <summary>Owning tenant. Set from tenant context on the HTTP path, explicitly in the reply job.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Master switch for the AI path. Off means the bot falls back to keyword rules alone — which is
    /// the behaviour that shipped before this engine existed, so switching it off is always safe.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>How the bot refers to the business, and what it says it is when asked.</summary>
    public string BusinessName { get; set; } = string.Empty;

    /// <summary>What the business does, in the owner's own words. The bulk of the model's grounding.</summary>
    public string BusinessDescription { get; set; } = string.Empty;

    /// <summary>Voice and register — "casual, use the customer's first name, keep it warm".</summary>
    public string? ToneNotes { get; set; }

    /// <summary>Address or directions, quoted verbatim when a customer asks where to go.</summary>
    public string? Location { get; set; }

    /// <summary>Phone, email or anything else worth handing out. Free text.</summary>
    public string? ContactNote { get; set; }

    /// <summary>
    /// How to write a service price when quoting one — "Rs.", "LKR", "$". <c>AppointmentType.Price</c>
    /// is a bare number and nothing else in the platform records the tenant's own currency (the Currency
    /// fields elsewhere are what the tenant pays US), so without this the bot would quote "3500" and
    /// leave the customer guessing.
    /// </summary>
    public string? CurrencyLabel { get; set; }

    /// <summary>Booking etiquette specific to this business — "always confirm the service first".</summary>
    public string? BookingInstructions { get; set; }

    /// <summary>
    /// The language the assistant always replies in, whatever the customer writes. Blank means mirror
    /// the customer instead.
    /// <para>
    /// Blank is only safe on a model that writes the customer's language well. Measured on
    /// claude-haiku-4-5, English is fluent but Sinhala and Singlish come out broken — so a fixed
    /// language here is the honest setting for most tenants on the cheap model.
    /// </para>
    /// </summary>
    public string? PreferredLanguage { get; set; }

    /// <summary>
    /// Ceiling on AI replies per company per day. A per-message LLM with no ceiling is an unbounded
    /// bill the moment a tenant is spammed or a loop appears; over the limit the bot degrades to the
    /// keyword rules rather than going quiet entirely.
    /// </summary>
    public int DailyReplyLimit { get; set; } = 500;

    /// <summary>
    /// The follow-up sequence, in order — "6 hours: offer a slot", "Day 2: check in", "Day 4: share
    /// results". Empty means a single nudge after six hours.
    /// <para>
    /// Steps due after Meta's 24-hour window cannot be sent as free text. Until template sending
    /// exists the job stops the sequence at the first step that falls outside the window.
    /// </para>
    /// </summary>
    public List<BotFollowUpStep> FollowUpSteps { get; set; } = [];

    /// <summary>
    /// Hours of complete silence after which a paused thread goes back to the bot. Zero means never.
    /// <para>
    /// The safety net under every takeover: an agent who steps in and moves on leaves the thread paused,
    /// and a pause with no end silently swallows every message the customer sends afterwards — observed
    /// live, five days of silence on a real enquiry. Counted from the last message either side sent, so
    /// an ongoing conversation never has the bot walk back in mid-sentence.
    /// </para>
    /// </summary>
    public int ResumeBotAfterIdleHours { get; set; } = 24;

    /// <summary>
    /// The team's WhatsApp numbers (digits only, country code first) that are told when the bot hands a
    /// customer to a person. Empty means nobody is messaged — the in-app notification still goes out.
    /// </summary>
    public List<string> HandoffAlertPhones { get; set; } = [];

    /// <summary>
    /// The approved template sent to <see cref="HandoffAlertPhones"/>. Three variables, in this order:
    /// the customer's name, their number, a one-line summary. Null means no WhatsApp alert.
    /// </summary>
    public Guid? HandoffAlertTemplateId { get; set; }

    /// <summary>
    /// Minutes before the bot takes a thread back after IT handed it to a person. Zero keeps the old
    /// behaviour — off until someone hands it back, or until the idle rule above expires it.
    /// </summary>
    public int HandoffResumeMinutes { get; set; }

    /// <summary>
    /// Rules that only apply to this kind of business, e.g. a clinic's "never give medical advice". The
    /// built-in rules are written for any business; anything specific to one trade goes here.
    /// </summary>
    public string? IndustryRules { get; set; }

    /// <summary>
    /// Whether the bot checks times and books, moves and cancels appointments. Off for a business that
    /// sells or takes orders: the bot then gets no booking tools and no booking rules, and moves the
    /// customer toward the next step in <see cref="BookingInstructions"/> instead.
    /// </summary>
    public bool BookingsEnabled { get; set; } = true;
}
