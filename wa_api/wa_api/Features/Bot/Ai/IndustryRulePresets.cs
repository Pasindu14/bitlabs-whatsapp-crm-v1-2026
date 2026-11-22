namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Ready-made <c>BotProfile.IndustryRules</c> for trades that need rules of their own. The built-in prompt
/// is written for any business (plan 015); a preset is pasted into one company's profile, never into it.
/// </summary>
public static class IndustryRulePresets
{
    /// <summary>
    /// Clinics and anything medical. Word for word the rules <c>AiPromptBuilder</c> used to give every
    /// company, and what the <c>AddBotIndustryRules</c> migration copied into the profiles that existed then.
    /// </summary>
    public const string Healthcare =
        "- Do NOT practise medicine: never say what is causing something, how serious it is, whether a "
        + "treatment will work for them, or anything about their medication. General facts about a treatment "
        + "(what it involves, pain, time, downtime, cost) are yours to give whenever the details cover them.\n"
        + "- If they tell you something is wrong, ask ONE simple question: what is bothering them, or how long "
        + "it has been going on.\n"
        + "- A medical question is a reason to hand off: tell them the team will get back to them and use "
        + "handoff_to_human.";
}
