"use client";

import { useRef, useState } from "react";
import { AlertCircle, RotateCcw, Send, Wrench, FlaskConical } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { SectionCard } from "./section-card";
import { usePreviewBot } from "@/features/bot/hooks/use-bot-profile";
import type { ActionFailure } from "@/lib/types/actions";
import { TOOL_LABELS, type BotPreviewToolCall, type BotPreviewTurn } from "@/features/bot/types";

interface PreviewMessage {
  isCustomer: boolean;
  text: string;
  toolCalls?: BotPreviewToolCall[];
  /** The run failed — rendered as an error rather than as something the assistant said. */
  failed?: boolean;
  code?: string;
  /** What producing this reply cost, shown under the LAST bubble of a run. */
  cost?: { usd: number | null; cachedReused: boolean };
}

const costFmt = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  minimumFractionDigits: 4,
  maximumFractionDigits: 4,
});

/**
 * Try the assistant before any customer does.
 *
 * Availability is read for real, so the times it offers here are genuine free slots — but nothing is
 * sent and nothing is booked. Without this, the only way to hear the assistant's voice would be to
 * switch it on and let it practise on real customers.
 */
export function AiPreviewPanel({ canPreview }: { canPreview: boolean }) {
  const preview = usePreviewBot();
  const [messages, setMessages] = useState<PreviewMessage[]>([]);
  const [draft, setDraft] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);

  function send() {
    const text = draft.trim();
    if (!text || preview.isPending) return;

    const next: PreviewMessage[] = [...messages, { isCustomer: true, text }];
    setMessages(next);
    setDraft("");

    const turns: BotPreviewTurn[] = next.map((m) => ({
      isCustomer: m.isCustomer,
      text: m.text,
    }));

    preview.mutate(turns, {
      onSuccess: (result) => {
        // Same pricing the live bot is billed at, so an owner can see what a reply really costs.
        const cost = {
          usd: result.costUsd ?? null,
          cachedReused: (result.cacheReadTokens ?? 0) > 0,
        };
        const last = result.bubbles.length - 1;
        setMessages((current) => [
          ...current,
          ...(result.bubbles.length > 0
            ? result.bubbles.map((text, i) => ({
                isCustomer: false,
                text,
                // Tool calls hang off the first bubble so the run shows once, not per message.
                toolCalls: i === 0 ? result.toolCalls : undefined,
                cost: i === last ? cost : undefined,
              }))
            : [
                {
                  isCustomer: false,
                  text: result.error ?? "(the assistant chose not to reply)",
                  toolCalls: result.toolCalls,
                  cost,
                },
              ]),
        ]);
        requestAnimationFrame(() => bottomRef.current?.scrollIntoView({ behavior: "smooth" }));
      },
      // A failed run has to be visible HERE, not only in a toast. Without this the panel showed the
      // customer's message and then nothing at all, which is indistinguishable from the assistant
      // deciding to stay quiet — and hides the one piece of information needed to fix it.
      onError: (error: ActionFailure) => {
        setMessages((current) => [
          ...current,
          {
            isCustomer: false,
            text: error.error ?? "Something went wrong and the assistant could not reply.",
            failed: true,
            code: error.code,
          },
        ]);
        requestAnimationFrame(() => bottomRef.current?.scrollIntoView({ behavior: "smooth" }));
      },
    });
  }

  return (
    <SectionCard
      title="Try it"
      icon={FlaskConical}
      tone="green"
      defaultOpen
      description={
        <>
          Talk to the assistant the way a customer would. It checks your real calendar, but nothing
          is sent and nothing is booked.
        </>
      }
      actions={
        messages.length > 0 ? (
          <Button type="button" variant="ghost" size="sm" onClick={() => setMessages([])}>
            <RotateCcw className="mr-2 size-4" />
            Start over
          </Button>
        ) : null
      }
      contentClassName="space-y-3"
    >
      {!canPreview ? (
        <p className="rounded-lg border border-dashed px-3 py-8 text-center text-sm text-muted-foreground">
          Save the business profile above first — the assistant needs to know what it&apos;s selling
          before it can answer anything.
        </p>
      ) : (
        <>
          <div className="max-h-96 space-y-2 overflow-y-auto rounded-lg border bg-muted/20 px-3 py-3">
            {messages.length === 0 ? (
              <p className="py-8 text-center text-sm text-muted-foreground">
                Try &ldquo;hi, do you build whatsapp bots?&rdquo; or ask for a time this week.
              </p>
            ) : (
              messages.map((m, i) => (
                <div key={i} className={m.isCustomer ? "flex justify-end" : "flex justify-start"}>
                  <div className="max-w-[80%] space-y-1.5">
                    {m.toolCalls && m.toolCalls.length > 0 ? (
                      <div className="space-y-1">
                        {m.toolCalls.map((call, j) => (
                          <p
                            key={j}
                            className="flex items-center gap-1.5 text-xs text-muted-foreground"
                          >
                            <Wrench className="size-3" />
                            {TOOL_LABELS[call.name] ?? call.name}
                          </p>
                        ))}
                      </div>
                    ) : null}
                    <div
                      className={`whitespace-pre-wrap rounded-2xl px-3 py-2 text-sm ${
                        m.failed
                          ? "border border-destructive/40 bg-destructive/10 text-destructive"
                          : m.isCustomer
                            ? "bg-primary text-primary-foreground"
                            : "border bg-card"
                      }`}
                    >
                      {m.failed ? (
                        <span className="flex items-start gap-2">
                          <AlertCircle className="mt-0.5 size-4 shrink-0" />
                          <span>
                            {m.text}
                            {m.code ? (
                              <span className="mt-1 block font-mono text-xs opacity-70">
                                {m.code}
                              </span>
                            ) : null}
                          </span>
                        </span>
                      ) : (
                        m.text
                      )}
                    </div>
                    {m.cost ? (
                      <p className="text-[11px] text-muted-foreground">
                        {m.cost.usd == null ? "Cost unknown" : `Cost ${costFmt.format(m.cost.usd)}`}
                        {" · "}
                        {m.cost.cachedReused ? "cached prompt reused" : "prompt cached fresh"}
                      </p>
                    ) : null}
                  </div>
                </div>
              ))
            )}

            {preview.isPending ? (
              <div className="flex justify-start">
                <div className="rounded-2xl border bg-card px-3 py-2">
                  <Spinner className="size-4" />
                </div>
              </div>
            ) : null}

            <div ref={bottomRef} />
          </div>

          <div className="flex items-end gap-2">
            <Textarea
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => {
                // Enter sends, Shift+Enter breaks the line — the same as the real composer.
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  send();
                }
              }}
              rows={2}
              placeholder="Type as if you were the customer…"
              className="resize-none"
            />
            <Button type="button" onClick={send} disabled={!draft.trim() || preview.isPending}>
              {preview.isPending ? <Spinner className="size-4" /> : <Send className="size-4" />}
            </Button>
          </div>
        </>
      )}
    </SectionCard>
  );
}
