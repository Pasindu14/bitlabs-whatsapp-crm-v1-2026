namespace wa_api.Features.Bot.Entities;

/// <summary>
/// One message in the follow-up sequence a quiet customer receives. Stored as JSON on
/// <see cref="BotProfile.FollowUpSteps"/>, in order.
/// <para>
/// <see cref="Message"/> is the owner's script, not the text sent: the model rewrites it in the
/// conversation's own voice and with the customer's name, keeping the meaning and dropping detail. A
/// fixed string sent verbatim reads as a broadcast; the same point made in context reads as a person.
/// </para>
/// </summary>
public class BotFollowUpStep
{
    /// <summary>
    /// Minutes after the assistant's last reply — when the silence began — that this step is due.
    /// Measured from the same anchor for every step, so "Day 4" means day four whatever happened to
    /// the steps before it.
    /// </summary>
    public int DelayMinutes { get; set; }

    /// <summary>
    /// What this step should say, in the owner's words. Used when the step lands INSIDE Meta's 24-hour
    /// window, where the model is free to rewrite it.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The approved template to send when this step falls OUTSIDE the window, where free text is
    /// impossible. Null means the step is simply skipped once the window has closed.
    /// <para>
    /// Both fields live on one step on purpose: which is used depends on the clock at the moment the
    /// step comes due, not on how the owner configured it.
    /// </para>
    /// </summary>
    public Guid? TemplateId { get; set; }

    /// <summary>
    /// Fixed values for the template's variables, in order from <c>{{2}}</c> — <c>{{1}}</c> is always the
    /// customer's first name. Anything left blank is resolved from the business profile at send time.
    /// <para>
    /// An override is the owner's escape hatch: a price or a deadline they want stated exactly, with no
    /// model involved.
    /// </para>
    /// </summary>
    public List<string> VariableOverrides { get; set; } = [];
}
