using wa_api.Features.Bot.Dtos;

namespace wa_api.Features.Bot;

/// <summary>
/// Runs the assistant against a made-up conversation so a tenant can tune it before it talks to
/// anyone real.
/// <para>
/// This is not a nicety. The engine is non-deterministic and the only other way to see what it says is
/// to message the business's own WhatsApp number and let it answer a customer — which means tuning the
/// persona in production, on real people. Availability is read for real (so the times it quotes are
/// genuine) but nothing is sent and nothing is booked.
/// </para>
/// </summary>
public interface IBotPreviewService
{
    Task<BotPreviewResponse> RunAsync(BotPreviewRequest request, CancellationToken ct = default);

    /// <summary>
    /// Exactly what the model is given for this business, built by the same code a live reply uses:
    /// the system prompt, the hand-off check, the follow-up variant and the tools. Read-only.
    /// </summary>
    Task<BotPromptResponse> GetPromptAsync(CancellationToken ct = default);
}
