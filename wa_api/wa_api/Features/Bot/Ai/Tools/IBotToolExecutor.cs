using System.Text.Json;

namespace wa_api.Features.Bot.Ai.Tools;

/// <summary>
/// What came back from a tool. <see cref="Content"/> is the text handed to the model — including for
/// failures, which is the whole point: "that slot has just been taken" is something the model can
/// recover from in conversation, whereas a thrown exception would kill the reply and leave the
/// customer with silence after they said yes.
/// </summary>
public sealed record BotToolResult(
    string Content, bool Booked = false, bool HandedOff = false, Guid? SharedLinkId = null);

/// <summary>
/// Runs the tools the model asks for.
/// <para>
/// Passed to the planner per call rather than injected, so the same planner drives both the live
/// reply job and the dry-run preview: the preview swaps in an executor that reads real availability
/// but only pretends to book. Without that split, tuning the persona would mean putting real
/// appointments on a real calendar.
/// </para>
/// </summary>
public interface IBotToolExecutor
{
    /// <summary>
    /// Executes one tool call. Must never throw for a bad argument or a refused action — an
    /// explanatory <see cref="BotToolResult"/> is always the better answer, because the model can act
    /// on it.
    /// </summary>
    Task<BotToolResult> ExecuteAsync(
        string toolName, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct);
}
