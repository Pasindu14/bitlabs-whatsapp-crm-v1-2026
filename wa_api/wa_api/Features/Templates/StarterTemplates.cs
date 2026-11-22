using wa_api.Features.Templates.Entities;

namespace wa_api.Features.Templates;

/// <summary>
/// A starting library of follow-up templates, written to be reusable by any business.
/// <para>
/// Deliberately generic: the wording is fixed (Meta approves wording, not intent) and everything
/// business-specific — the service, the offer, the deadline, the person — travels as a variable. Six
/// marketing shapes cover every re-engagement pattern a sequence needs: check in, qualify, prove, get
/// personal, name a deadline, let go. A new client is then configuration rather than six more approvals.
/// </para>
/// <para>
/// These are created as DRAFTS and never submitted here. Each client submits under their own number and
/// their own credentials, so pressing submit has to stay theirs.
/// </para>
/// </summary>
public static class StarterTemplates
{
    /// <summary>One entry in the library, plus the plain-English meaning of each variable.</summary>
    public sealed record Starter(
        string Name,
        TemplateCategory Category,
        string Description,
        TemplateComponents Components,
        IReadOnlyList<string> VariableHints
    );

    /// <summary>
    /// What each variable means, in the order Meta numbers them. Used to tell the model what to fill and
    /// to label the override inputs — <c>{{1}}</c> is always the customer's first name.
    /// </summary>
    private const string CustomerFirstName = "the customer's first name";

    public static IReadOnlyList<Starter> All { get; } =
    [
        new(
            "follow_up_checkin",
            TemplateCategory.Marketing,
            "A soft nudge a day or two after they went quiet.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, just checking in on your {{2}} enquiry. Any questions I can help with?",
                    Examples = ["Sarah", "membership"],
                },
                Buttons =
                [
                    new TemplateButton { Type = "quick_reply", Text = "Yes, I have a question" },
                ],
            },
            [CustomerFirstName, "what they enquired about, e.g. \"membership\""]),

        new(
            "follow_up_qualify",
            TemplateCategory.Marketing,
            "Asks what they are trying to sort out, so the reply can be useful.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, so I can point you in the right direction, could I ask what you're "
                         + "looking to sort out? Our {{2}} can then advise you properly.",
                    Examples = ["Sarah", "team"],
                },
            },
            [CustomerFirstName, "who advises them, e.g. \"sales team\""]),

        new(
            "follow_up_proof",
            TemplateCategory.Marketing,
            "Shares real results or reviews. The link travels as a button.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, sharing a few real results from customers like you, "
                         + "so you can see what to expect.",
                    Examples = ["Sarah"],
                },
                Buttons =
                [
                    // A URL button rather than a link in the text: it survives Meta's review more easily
                    // and a parameter cannot carry a long URL safely.
                    new TemplateButton
                    {
                        Type = "url",
                        Text = "See results",
                        Url = "https://example.com/results",
                    },
                ],
            },
            [CustomerFirstName]),

        new(
            "follow_up_personal",
            TemplateCategory.Marketing,
            "The warm, no-pressure message about a week in.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, I saw you'd been asking about {{2}} and wanted to say hello "
                         + "personally.\n\nNo pressure at all — we'd love to meet you and talk through your "
                         + "options whenever you feel ready.",
                    Examples = ["Sarah", "our weekend classes"],
                },
            },
            [CustomerFirstName, "what they asked about"]),

        new(
            "follow_up_offer",
            TemplateCategory.Marketing,
            "A real deadline plus payment options. Price and date are variables, so they change without a new approval.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, quick heads-up: our {{2}} offer ends on {{3}}. You can also split the "
                         + "cost with {{4}}. Shall I reserve your spot?",
                    Examples = ["Sarah", "20% off any package", "30 September", "an instalment plan"],
                },
                Buttons =
                [
                    new TemplateButton { Type = "quick_reply", Text = "Reserve my spot" },
                ],
            },
            [
                CustomerFirstName,
                "the offer, e.g. \"20% off any package\"",
                "the date it ends",
                "payment options, e.g. \"an instalment plan\"",
            ]),

        new(
            "follow_up_gentle",
            TemplateCategory.Marketing,
            "The take-away: no pressure, door left open. The last step of a sequence.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, hope you're doing well. No rush at all, just a gentle check-in.\n\n"
                         + "Whenever the timing feels right, {{2}} and the team would be happy to meet you.",
                    Examples = ["Sarah", "Priya"],
                },
            },
            [CustomerFirstName, "the owner's name or the business name"]),

        // ── Utility: only for customers who have actually booked ──────────────
        // Far cheaper than Marketing, and legitimately Utility because each one is about a booking the
        // customer made themselves.
        new(
            "appointment_confirmed",
            TemplateCategory.Utility,
            "Sent when a booking is made. Utility, so it costs a fraction of a marketing message.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, your {{2}} is confirmed for {{3}} at {{4}}. See you then.",
                    Examples = ["Sarah", "appointment", "Monday 29 September", "5:00 pm"],
                },
            },
            [CustomerFirstName, "what was booked", "the date", "the time"]),

        new(
            "appointment_reminder",
            TemplateCategory.Utility,
            "The day-before reminder. Utility, and the cheapest message on the platform.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "Hi {{1}}, a reminder about your {{2}} tomorrow at {{3}}. Reply here if you need "
                         + "to change it.",
                    Examples = ["Sarah", "appointment", "5:00 pm"],
                },
            },
            [CustomerFirstName, "what was booked", "the time"]),

        // ── Utility: sent to the TEAM, not to a customer ──────────────────────
        // The bot hands a chat to a person and goes quiet, so someone has to be told. Staff rarely have the
        // 24-hour window open with the business number, so the alert has to be a template. Utility: it is an
        // operational message about a customer who is waiting, and it carries no offer.
        new(
            StaffHandoffAlertName,
            TemplateCategory.Utility,
            "Sent to your team's WhatsApp when the bot hands a customer over. Pick it under Bot settings.",
            new TemplateComponents
            {
                Body = new TemplateBody
                {
                    Text = "A customer needs the team: {{1}}, +{{2}}. Summary: {{3}}. Please contact them as "
                         + "soon as you can.",
                    Examples =
                    [
                        "Sarah",
                        "94771234567",
                        "Asked about the price of a package and wants 2pm tomorrow",
                    ],
                },
            },
            ["the customer's name", "their WhatsApp number, digits only", "a one-line summary of what they need"]),
    ];

    /// <summary>The library's alert for the team. Named once so the settings screen can recognise it.</summary>
    public const string StaffHandoffAlertName = "staff_handoff_alert";

    /// <summary>A deep copy, so the caller can edit one without mutating the shared library.</summary>
    public static TemplateComponents CopyComponents(TemplateComponents source) => new()
    {
        Header = source.Header is null ? null : new TemplateHeader
        {
            Type = source.Header.Type,
            Text = source.Header.Text,
            TextExample = source.Header.TextExample,
        },
        Body = new TemplateBody { Text = source.Body.Text, Examples = [.. source.Body.Examples] },
        Footer = source.Footer is null ? null : new TemplateFooter { Text = source.Footer.Text },
        Buttons =
        [
            .. source.Buttons.Select(b => new TemplateButton
            {
                Type = b.Type,
                Text = b.Text,
                Url = b.Url,
                UrlExample = b.UrlExample,
                PhoneNumber = b.PhoneNumber,
            }),
        ],
    };

    /// <summary>The variable hints for a template we created from the library, or empty if it is not ours.</summary>
    public static IReadOnlyList<string> HintsFor(string templateName) =>
        All.FirstOrDefault(s => s.Name == templateName)?.VariableHints ?? [];
}
