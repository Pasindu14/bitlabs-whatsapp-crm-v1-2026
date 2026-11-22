using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using wa_api.Features.Bot.Ai.Tools;

namespace wa_api.Features.Bot.Ai;

/// <summary>
/// Runs the Claude conversation: build the prompt, call the model, execute whatever tools it asks for,
/// loop until it answers in text.
/// <para>
/// No thinking is requested. Haiku 4.5 takes a fixed <c>budget_tokens</c> rather than adaptive
/// thinking, and reasoning buys nothing on a two-sentence sales reply while costing latency the
/// customer feels on the other end of a WhatsApp thread.
/// </para>
/// <para>
/// Not streamed, deliberately: replies are a few hundred tokens, this runs in a background job with no
/// UI attached, and the whole message has to exist before the first WhatsApp send anyway.
/// </para>
/// </summary>
public sealed class AiReplyPlanner : IAiReplyPlanner
{
    /// <summary>
    /// Tool round-trips one reply may take before the model must answer in text. Bounds both latency and
    /// the cost of a turn stuck re-checking availability.
    /// </summary>
    public const int MaxToolIterations = 3;

    private readonly AnthropicOptions _options;
    private readonly ILogger<AiReplyPlanner> _logger;
    private readonly AnthropicClient? _client;

    public AiReplyPlanner(
        IOptions<AnthropicOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AiReplyPlanner> logger)
    {
        _options = options.Value;
        _logger = logger;

        // No key is a supported state, not a startup failure — the bot falls back to keyword rules,
        // which is exactly how it behaved before this engine existed.
        if (!_options.IsConfigured) return;

        _client = new AnthropicClient
        {
            ApiKey = _options.ApiKey,
            HttpClient = httpClientFactory.CreateClient(AnthropicOptions.HttpClientName),
        };
    }

    public bool IsConfigured => _client is not null;

    public string Model => _options.Model;

    public async Task<AiAskResult> AskAsync(
        string system, string question, int maxTokens, CancellationToken ct = default)
    {
        if (_client is null) return AiAskResult.Unavailable;

        try
        {
            var response = await _client.Messages.Create(
                new MessageCreateParams
                {
                    Model = _options.Model,
                    MaxTokens = maxTokens,
                    System = system,
                    // No tools on purpose: this asks the model to read the business details and answer,
                    // not to look anything up or do anything.
                    Messages = [new MessageParam { Role = Role.User, Content = question }],
                },
                cancellationToken: ct);

            var (text, _) = Unpack(response);
            var usage = new AiUsage(
                (int)response.Usage.InputTokens,
                (int)response.Usage.OutputTokens,
                (int)(response.Usage.CacheReadInputTokens ?? 0),
                (int)(response.Usage.CacheCreationInputTokens ?? 0));

            return new AiAskResult(string.IsNullOrWhiteSpace(text) ? null : text.Trim(), usage);
        }
        catch (Exception ex)
        {
            // Null means "do not send", which is the safe outcome for a template whose values we could
            // not establish.
            _logger.LogWarning(ex, "Could not reach Anthropic to fill template variables.");
            return AiAskResult.Unavailable;
        }
    }

    public async Task<AiReplyPlan> PlanAsync(
        AiTurnContext ctx, IBotToolExecutor tools, int maxToolIterations, CancellationToken ct = default)
    {
        if (_client is null) return AiReplyPlan.Failed("Anthropic API key is not configured.");

        var stopwatch = Stopwatch.StartNew();
        var messages = BuildMessages(ctx);
        var invocations = new List<AiToolInvocation>();

        var inputTokens = 0;
        var outputTokens = 0;
        var cacheReadTokens = 0;
        var cacheWriteTokens = 0;
        var iterations = 0;
        var handedOff = false;
        var booked = false;
        var sharedLinkIds = new List<Guid>();

        // Text the model wrote alongside a tool call. It is part of the reply, not scaffolding: the model
        // routinely answers the question and calls share_link in the same response, then has nothing left
        // to say after the tool result. Keeping only the final pass's text dropped that answer and sent
        // the customer a bare link in reply to "what is your location".
        var reply = new StringBuilder();

        try
        {
            // Before the model writes anything: can this be answered from what it knows? Asked on its own,
            // with the full business details in view, Haiku answers that reliably; the same instruction
            // buried among the other rules in the reply prompt was ignored twice on the live account.
            if (!ctx.IsFollowUp && LastCustomerMessage(ctx) is not null)
            {
                var gate = await GateAsync(ctx, messages, ct);
                inputTokens += gate.Usage.InputTokens;
                outputTokens += gate.Usage.OutputTokens;
                cacheReadTokens += gate.Usage.CacheReadTokens;
                cacheWriteTokens += gate.Usage.CacheWriteTokens;

                if (IsHandoffVerdict(gate.Text) && await HandOffAsync(tools, invocations, ct))
                {
                    _logger.LogInformation("Handed off a message the assistant could not answer.");
                    return new AiReplyPlan(
                        BubbleSplitter.Split(HoldingMessageFor(gate.Text)), invocations, true, false,
                        inputTokens, outputTokens, cacheReadTokens, 0, null, sharedLinkIds, cacheWriteTokens);
                }
            }

            // One extra pass beyond the tool budget: the last one runs with tools switched off, so the
            // model always gets a turn to actually SAY something. Without it, a model that spent its
            // whole budget on tool calls would leave the customer with silence.
            for (var pass = 0; pass <= maxToolIterations; pass++)
            {
                var toolsExhausted = pass == maxToolIterations;

                var response = await _client.Messages.Create(
                    new MessageCreateParams
                    {
                        Model = _options.Model,
                        MaxTokens = _options.MaxTokens,
                        System = BuildSystem(ctx),
                        Tools = [.. BotTools.For(ctx.Business.BookingsEnabled)],
                        ToolChoice = toolsExhausted
                            ? new ToolChoiceNone()
                            : new ToolChoiceAuto(),
                        Messages = messages,
                    },
                    cancellationToken: ct);

                inputTokens += (int)response.Usage.InputTokens;
                outputTokens += (int)response.Usage.OutputTokens;
                cacheReadTokens += (int)(response.Usage.CacheReadInputTokens ?? 0);
                cacheWriteTokens += (int)(response.Usage.CacheCreationInputTokens ?? 0);

                var (text, toolUses) = Unpack(response);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (reply.Length > 0) reply.Append("\n\n");
                    reply.Append(text.Trim());
                }

                if (toolUses.Count == 0 || toolsExhausted)
                {
                    // When the model hands off itself, the customer gets the standard holding message, not
                    // whatever it wrote beside the tool call (observed live: "Someone from the team will
                    // get back to you shortly.").
                    if (handedOff && !booked) reply.Clear().Append(HoldingMessage);

                    return new AiReplyPlan(
                        BubbleSplitter.Split(reply.ToString()), invocations, handedOff, booked,
                        inputTokens, outputTokens, cacheReadTokens, iterations, null, sharedLinkIds, cacheWriteTokens);
                }

                iterations++;

                // Echo the assistant turn back verbatim, then answer EVERY tool_use in one user
                // message — the API rejects the follow-up if any tool_use id lacks a result.
                var assistantContent = new List<ContentBlockParam>();
                if (!string.IsNullOrWhiteSpace(text)) assistantContent.Add(new TextBlockParam { Text = text });

                var toolResults = new List<ContentBlockParam>();
                foreach (var use in toolUses)
                {
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID = use.ID,
                        Name = use.Name,
                        Input = use.Input,
                    });

                    // Belt and braces: a business without bookings is never sent the booking tools, but a
                    // model that calls one anyway must not book anything.
                    var result = BotTools.IsAllowed(use.Name, ctx.Business.BookingsEnabled)
                        ? await tools.ExecuteAsync(use.Name, use.Input, ct)
                        : new BotToolResult(
                            $"There is no tool called {use.Name}. This business does not take bookings here.");
                    handedOff |= result.HandedOff;
                    booked |= result.Booked;
                    if (result.SharedLinkId is { } linkId && !sharedLinkIds.Contains(linkId))
                        sharedLinkIds.Add(linkId);

                    invocations.Add(new AiToolInvocation(
                        use.Name, JsonSerializer.Serialize(use.Input), result.Content));

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = use.ID,
                        Content = result.Content,
                    });
                }

                messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });
                messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
            }

            // Unreachable: the final pass above always returns. Kept so the compiler sees a total function.
            return new AiReplyPlan(
                [], invocations, handedOff, booked,
                inputTokens, outputTokens, cacheReadTokens, iterations, null, sharedLinkIds, cacheWriteTokens);
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or TaskCanceledException
                                      && !ct.IsCancellationRequested)
        {
            // Degrade to silence, never to a half-written message. The caller falls back to the
            // tenant's Fallback rule if they have one.
            _logger.LogWarning(ex,
                "AI reply planning failed after {Iterations} tool iterations in {ElapsedMs}ms",
                iterations, stopwatch.ElapsedMilliseconds);

            return new AiReplyPlan(
                [], invocations, handedOff, booked,
                inputTokens, outputTokens, cacheReadTokens, iterations, Describe(ex), sharedLinkIds, cacheWriteTokens);
        }
    }

    /// <summary>What the customer sees when a person is brought in instead of the model's non-answer.</summary>
    public const string HoldingMessage =
        "Our dedicated customer service specialist will assist you. Kindly wait for few moments.";

    /// <summary>
    /// The owner's wording: "For HFC, our dedicated customer service specialist will assist you." The
    /// topic is whatever the gate named after HANDOFF; with none, the sentence stands without it.
    /// </summary>
    public static string HoldingMessageFor(string? verdict)
    {
        if (verdict is null) return HoldingMessage;
        var at = verdict.IndexOf("HANDOFF", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return HoldingMessage;

        var topic = verdict[(at + "HANDOFF".Length)..].Split('\n')[0].Trim(' ', ':', '-', '.', '"', '\'', '<', '>');
        return topic.Length is 0 or > 40
            ? HoldingMessage
            : $"For {topic}, our dedicated customer service specialist will assist you. Kindly wait for few moments.";
    }

    // Third system block, after the cached ones, so the business details prefix is still a cache hit.
    public const string GateInstruction =
        "DECISION FOR THIS TURN ONLY. Do not write a reply to the customer. Read their latest message and "
        + "answer with ANSWER, or with HANDOFF followed by the service, product or topic they asked about "
        + "in one to three words (for example: HANDOFF HFC). Nothing else.\n"
        + "HANDOFF when answering it needs something you do not have: a service or product that is not in "
        + "the details above (even if you think the business might offer it), a comparison or "
        + "recommendation between them, a refund or billing question, a price, time or policy that is not "
        + "in the details, anything the business's own rules above say to hand off, anything you are not "
        + "certain about, or they ask for a person.\n"
        + "ANSWER when you can answer it fully from the details above or with a tool, or it is a greeting, "
        + "thanks, a goodbye, a step in booking (a time, a name, a confirmation), or a request to cancel "
        + "or move a booking.";


    private async Task<AiAskResult> GateAsync(
        AiTurnContext ctx, List<MessageParam> messages, CancellationToken ct)
    {
        try
        {
            // Same tools and same first two system blocks as the reply call, or the prompt cache misses.
            var system = BuildSystemBlocks(ctx);
            system.Add(new TextBlockParam { Text = GateInstruction });

            var response = await _client!.Messages.Create(
                new MessageCreateParams
                {
                    Model = _options.Model,
                    MaxTokens = 16,
                    System = system,
                    Tools = [.. BotTools.For(ctx.Business.BookingsEnabled)],
                    ToolChoice = new ToolChoiceNone(),
                    Messages = messages,
                },
                cancellationToken: ct);

            var (text, _) = Unpack(response);
            var usage = new AiUsage(
                (int)response.Usage.InputTokens,
                (int)response.Usage.OutputTokens,
                (int)(response.Usage.CacheReadInputTokens ?? 0),
                (int)(response.Usage.CacheCreationInputTokens ?? 0));
            return new AiAskResult(string.IsNullOrWhiteSpace(text) ? null : text, usage);
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or TaskCanceledException
                                      && !ct.IsCancellationRequested)
        {
            // A failed check must not cost the customer a reply: carry on as if it passed.
            _logger.LogWarning(ex, "Could not run the can-I-answer-this check.");
            return AiAskResult.Unavailable;
        }
    }

    private static async Task<bool> HandOffAsync(
        IBotToolExecutor tools, List<AiToolInvocation> invocations, CancellationToken ct)
    {
        var input = new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement(
                "The assistant could not answer this, so it was handed over automatically."),
        };
        var result = await tools.ExecuteAsync(BotTools.HandoffToHuman, input, ct);
        invocations.Add(new AiToolInvocation(
            BotTools.HandoffToHuman, JsonSerializer.Serialize(input), result.Content));
        return result.HandedOff;
    }

    public static bool IsHandoffVerdict(string? verdict) =>
        verdict is not null && verdict.TrimStart().StartsWith("HANDOFF", StringComparison.OrdinalIgnoreCase);

    private static string? LastCustomerMessage(AiTurnContext ctx) =>
        ctx.Transcript.LastOrDefault(t => t.IsCustomer)?.Text is { Length: > 0 } text ? text : null;

    /// <summary>
    /// Two system blocks with the cache breakpoint between them: the business facts are stable and get
    /// <c>cache_control</c>, the clock and the customer's name come after it. The order matters — a
    /// prefix cache is a byte-prefix match, so anything volatile placed before the breakpoint would
    /// mean every request paid full price with no visible symptom.
    /// </summary>
    private static MessageCreateParamsSystem BuildSystem(AiTurnContext ctx) => BuildSystemBlocks(ctx);

    private static List<TextBlockParam> BuildSystemBlocks(AiTurnContext ctx) =>
    [
        new()
        {
            Text = AiPromptBuilder.BuildStableSystem(ctx.Business),
            CacheControl = new CacheControlEphemeral(),
        },
        new() { Text = AiPromptBuilder.BuildVolatileSystem(ctx) },
    ];

    /// <summary>
    /// The thread as alternating turns. Consecutive messages in the same direction are merged, because
    /// a customer firing off three messages in a row is normal on WhatsApp and the API requires the
    /// roles to alternate.
    /// </summary>
    private static List<MessageParam> BuildMessages(AiTurnContext ctx) =>
        [.. AiPromptBuilder.BuildTurns(ctx.Transcript).Select(turn => new MessageParam
        {
            Role = turn.IsCustomer ? Role.User : Role.Assistant,
            Content = turn.Text,
        })];

    /// <summary>Splits a response into its text and its tool calls.</summary>
    private static (string Text, List<ToolUseBlock> ToolUses) Unpack(Message response)
    {
        var text = new StringBuilder();
        var toolUses = new List<ToolUseBlock>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? t))
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(t.Text);
            }
            else if (block.TryPickToolUse(out ToolUseBlock? use))
            {
                toolUses.Add(use);
            }
        }

        return (text.ToString().Trim(), toolUses);
    }

    /// <summary>
    /// A short reason for the log. Never the raw exception text — it can carry request fragments, and
    /// this string is stored on a row a tenant's own screen may render.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        AnthropicRateLimitException => "Rate limited by Anthropic.",
        AnthropicApiException api => $"Anthropic returned an error ({api.GetType().Name}).",
        TaskCanceledException => "Anthropic request timed out.",
        _ => "Could not reach Anthropic.",
    };
}
