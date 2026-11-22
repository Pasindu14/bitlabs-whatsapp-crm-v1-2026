"use client";

import { useState } from "react";
import { Check, Copy } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useBotPrompt } from "@/features/bot/hooks/use-bot-profile";
import { SectionCard } from "./section-card";

/**
 * Read-only view of exactly what the AI is given for this business, built by the same server code a
 * live reply uses (AiPromptBuilder for the prompt, AiReplyPlanner for the steps around it). Nothing is
 * editable here: change the AI assistant tab and this updates.
 */
export function PromptView() {
  const { data: prompt, isLoading, isError } = useBotPrompt();

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-24 w-full rounded-xl" />
        <Skeleton className="h-64 w-full rounded-xl" />
      </div>
    );
  }

  if (isError || !prompt) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Could not load the prompt. Save the business profile on the AI assistant tab first, then
          refresh.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-6">
      <SectionCard
        title="How a reply is made (reply planner)"
        description={
          <>
            What happens on the server every time a customer writes. Model:{" "}
            <span className="font-mono">{prompt.model}</span>
          </>
        }
        defaultOpen
      >
        <ol className="list-decimal space-y-2 pl-5 text-sm">
          <li>
            <span className="font-medium">Hand-off check.</span> A separate short AI call reads the
            customer&apos;s message with the full prompt below plus the hand-off check instruction, and
            answers ANSWER or HANDOFF. On HANDOFF the chat goes to your team and the customer gets the
            hand-off message. No reply is written.
          </li>
          <li>
            <span className="font-medium">Reply.</span> The AI gets the tools, the fixed prompt, the
            per-message prompt and the chat so far. It may use tools up to{" "}
            {prompt.maxToolIterations} times (check times, book, share a link…). The last round has
            tools switched off, so it always ends with words.
          </li>
          <li>
            <span className="font-medium">Hand-off by the AI.</span> If it calls handoff_to_human, its
            words are replaced with the hand-off message.
          </li>
          <li>
            <span className="font-medium">Send.</span> The reply is split into at most{" "}
            {prompt.maxMessages} WhatsApp messages of up to {prompt.maxMessageLength} characters each.
          </li>
          <li>
            <span className="font-medium">Follow-ups</span> use the same prompt, with the follow-up
            version of the per-message part below.
          </li>
        </ol>
      </SectionCard>

      <SectionCard
        title="Fixed prompt (prompt builder)"
        description="The rules plus everything from the AI assistant tab: business details, booking rules, prepared answers, links, services and opening hours. The same for every message, so it is cached."
      >
        <PromptBlock text={prompt.stableSystem} />
      </SectionCard>

      <SectionCard
        title="Per-message prompt"
        description="Rebuilt for every message: the time, the next two weeks of dates, and the customer's name. Shown here for a new customer whose name is not known yet."
      >
        <PromptBlock text={prompt.volatileSystem} />
      </SectionCard>

      <SectionCard
        title="Per-message prompt for a follow-up"
        description="Used instead of the one above when the customer went quiet and a follow-up is due. Shown with your first follow-up message."
      >
        <PromptBlock text={prompt.followUpVolatileSystem} />
      </SectionCard>

      <SectionCard
        title="Hand-off check (reply planner)"
        description="Added to the fixed prompt for the separate check that runs before every reply."
      >
        <PromptBlock text={prompt.gateInstruction} />
        <div className="space-y-1.5">
          <p className="text-sm font-medium">Message the customer gets on hand-off</p>
          <PromptBlock text={prompt.holdingMessage} />
        </div>
      </SectionCard>

      <SectionCard
        title="Tools the AI can use"
        description="Sent with every reply. The AI decides when to call them; the server checks every call."
      >
        <ul className="space-y-3">
          {prompt.tools.map((tool) => (
            <li key={tool.name} className="space-y-1 rounded-lg border p-3">
              <Badge variant="secondary" className="font-mono">
                {tool.name}
              </Badge>
              <p className="text-sm text-muted-foreground">{tool.description}</p>
            </li>
          ))}
        </ul>
      </SectionCard>
    </div>
  );
}

function PromptBlock({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      toast.error("Could not copy. Select the text and copy it instead.");
    }
  };

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between text-xs text-muted-foreground">
        <span>{text.length.toLocaleString()} characters</span>
        <Button type="button" variant="ghost" size="sm" onClick={copy}>
          {copied ? <Check className="mr-2 size-4" /> : <Copy className="mr-2 size-4" />}
          {copied ? "Copied" : "Copy"}
        </Button>
      </div>
      <pre className="max-h-[32rem] overflow-auto rounded-lg border bg-muted/40 p-3 font-mono text-xs leading-relaxed whitespace-pre-wrap">
        {text}
      </pre>
    </div>
  );
}
