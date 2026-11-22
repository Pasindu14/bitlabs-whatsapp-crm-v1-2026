"use client";

import { Fragment, useEffect, useRef, useState } from "react";
import { ArrowLeft, Bot, Clock, User } from "lucide-react";
import { DoubleTick } from "@/components/brand-mark";
import { cn } from "@/lib/utils";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Spinner } from "@/components/ui/spinner";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import {
  useConversationMessages,
  useSetConversationBotState,
} from "@/features/conversations/hooks/use-conversations";
import type { ConversationBotState } from "@/features/conversations/types";
import { useBotAccess } from "@/features/bot/hooks/use-bot-access";
import { MessageBubble } from "./message-bubble";
import { MessageComposer } from "./message-composer";
import { initials, formatDayDivider, sameDay, displayPhone, avatarTone, windowLeft } from "./format";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";

export function MessageThread() {
  const selected = useConversationStore((s) => s.selected);
  const select = useConversationStore((s) => s.select);
  const botTyping = useConversationStore((s) => (selected ? !!s.botTyping[selected.id] : false));
  const { data: messages, isLoading } = useConversationMessages(selected?.id ?? null);
  const bottomRef = useRef<HTMLDivElement>(null);
  // Companies without the chatbot never see "the bot is handling this chat".
  const botAccess = useBotAccess();

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages?.length, selected?.id]);

  if (!selected) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-4 bg-[#eef4ef] px-6 text-center dark:bg-muted/20">
        <span className="flex size-16 items-center justify-center rounded-2xl bg-card text-primary shadow-[0_12px_30px_-14px_rgb(16_60_36/0.35)]">
          <DoubleTick className="w-8" />
        </span>
        <div className="space-y-1.5">
          <p className="text-lg font-medium tracking-tight">Pick a chat to start replying</p>
          <p className="max-w-xs text-sm text-muted-foreground">
            Unread chats are marked in green. Your replies go out from your WhatsApp business number.
          </p>
        </div>
      </div>
    );
  }

  const windowOpen = selected.windowExpiresAt ? new Date(selected.windowExpiresAt) > new Date() : false;
  const left = windowLeft(selected.windowExpiresAt);
  const name = selected.contactName || displayPhone(selected.contactPhone);

  return (
    <div className="flex h-full flex-col bg-[#eef4ef] dark:bg-muted/20">
      <div className="flex h-16 shrink-0 items-center gap-3 border-b bg-card px-3 sm:px-5">
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={() => select(null)}
          aria-label="Back to inbox"
        >
          <ArrowLeft className="size-5" />
        </Button>
        <Avatar className="size-10">
          <AvatarFallback className={cn("text-sm font-medium", avatarTone(name))}>{initials(name)}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <p className="truncate text-[15px] font-medium">{name}</p>
          <p className="truncate text-xs text-muted-foreground">
            {displayPhone(selected.contactPhone)}, via {selected.displayPhoneNumber}
          </p>
        </div>

        {botTyping ? <BotWorkingBadge /> : null}

        <span
          title="WhatsApp lets you reply freely for 24 hours after the customer's last message."
          className={cn(
            "hidden shrink-0 items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium sm:flex",
            left ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground",
          )}
        >
          <Clock className="size-3.5" />
          {left ? `Reply window: ${left}` : "Reply window closed"}
        </span>
      </div>

      <ScrollArea className="min-h-0 flex-1">
        <div className="mx-auto w-full max-w-3xl px-3 sm:px-6">
          {isLoading ? (
            <div className="flex h-40 items-center justify-center">
              <Spinner className="size-5 text-muted-foreground" />
            </div>
          ) : !messages || messages.length === 0 ? (
            <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">
              No messages in this chat yet.
            </div>
          ) : (
            <div className="flex flex-col gap-1.5 py-5">
              {messages.map((m, i) => {
                const showDivider = i === 0 || !sameDay(messages[i - 1].createdAt, m.createdAt);
                return (
                  <Fragment key={m.id}>
                    {showDivider && (
                      <div className="my-3 flex justify-center">
                        <span className="rounded-full bg-card/90 px-3 py-1 text-[11px] font-medium text-muted-foreground shadow-[0_1px_0_rgb(0_0_0/0.05)]">
                          {formatDayDivider(m.createdAt)}
                        </span>
                      </div>
                    )}
                    <MessageBubble message={m} conversationId={selected.id} />
                  </Fragment>
                );
              })}
              <div ref={bottomRef} />
            </div>
          )}
        </div>
      </ScrollArea>

      <div className="mx-auto w-full max-w-3xl shrink-0 space-y-2 px-3 pb-3 sm:px-6 sm:pb-4">
        {botAccess.chatbot && botAccess.platformEnabled ? (
          <BotStateBanner
            conversationId={selected.id}
            botState={selected.botState}
            botResumesAt={selected.botResumesAt}
            botPausedByAgent={selected.botPausedByAgent}
          />
        ) : null}

        <MessageComposer conversationId={selected.id} windowOpen={windowOpen} contactName={name} />
      </div>
    </div>
  );
}

/**
 * Shown in the thread header while the assistant is composing. The reply is several seconds away —
 * the model call, any calendar lookups, and the deliberate read delay — and without this the inbox
 * looks like it simply did not react to the customer's message.
 */
function BotWorkingBadge() {
  return (
    <span className="flex shrink-0 items-center gap-2 rounded-full border bg-muted/60 px-2.5 py-1 text-xs text-muted-foreground">
      <Spinner className="size-3" />
      <span className="hidden sm:inline">Bot is replying</span>
    </span>
  );
}

/**
 * Who is answering this thread — the bot, or you.
 *
 * The composer stays enabled either way: typing IS the takeover, and the API pauses the bot on the
 * first manual send. This just makes that visible, and gives you a way to claim a thread before
 * saying anything, or hand it back when you're done.
 */
function BotStateBanner({
  conversationId,
  botState,
  botResumesAt,
  botPausedByAgent,
}: {
  conversationId: string;
  botState: ConversationBotState;
  botResumesAt: string | null;
  botPausedByAgent: boolean;
}) {
  const setBotState = useSetConversationBotState();
  const [asking, setAsking] = useState(false);
  const paused = botState === "Paused";
  // The bot pausing itself is a handoff: it told the customer someone would be in touch.
  const handedOver = paused && !botPausedByAgent;

  return (
    <div
      className={cn(
        "flex items-center justify-between gap-3 rounded-xl px-3 py-2 text-xs",
        paused ? "bg-amber-50 text-amber-900 ring-1 ring-amber-200/70 dark:bg-amber-500/10 dark:text-amber-200 dark:ring-amber-500/20" : "bg-card text-foreground ring-1 ring-black/5",
      )}
    >
      <span className="flex min-w-0 items-center gap-2">
        <span
          className={cn(
            "flex size-6 shrink-0 items-center justify-center rounded-full",
            paused ? "bg-amber-100 text-amber-700 dark:bg-amber-500/20" : "bg-primary/10 text-primary",
          )}
        >
          {paused ? <User className="size-3.5" /> : <Bot className="size-3.5" />}
        </span>
        {paused ? (
          <>
            {handedOver ? "The bot passed this chat to your team" : "You're handling this chat"}
            {botResumesAt
              ? ` — the bot takes over again ${formatResume(botResumesAt)}`
              : " — the bot stays off until you hand it back"}
          </>
        ) : (
          <>The bot is answering this chat</>
        )}
      </span>

      <div className="flex items-center gap-1">
        {paused ? (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={setBotState.isPending}
            onClick={() => setAsking(true)}
          >
            Hand back in…
          </Button>
        ) : null}
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="rounded-full bg-card"
          disabled={setBotState.isPending}
          onClick={() =>
            paused ? setBotState.mutate({ conversationId, paused: false }) : setAsking(true)
          }
        >
          {paused ? "Hand back to bot" : "Take over"}
        </Button>
      </div>

      <TakeOverDialog
        open={asking}
        onOpenChange={setAsking}
        pending={setBotState.isPending}
        handBack={paused}
        onConfirm={(resumeAfterMinutes) => {
          setBotState.mutate({ conversationId, paused: true, resumeAfterMinutes });
          setAsking(false);
        }}
      />
    </div>
  );
}

/**
 * How long the bot stands down for. Undefined is "until I say otherwise" — still offered, because a
 * real escalation needs it, but never the default: a pause with no end is how a thread goes silent
 * for days without anyone noticing.
 */
const TAKEOVER_OPTIONS: { label: string; minutes?: number }[] = [
  { label: "30 minutes", minutes: 30 },
  { label: "1 hour", minutes: 60 },
  { label: "6 hours", minutes: 360 },
  { label: "12 hours", minutes: 720 },
  { label: "24 hours", minutes: 1440 },
  { label: "Until I hand it back", minutes: undefined },
];

function TakeOverDialog({
  open,
  onOpenChange,
  pending,
  handBack = false,
  onConfirm,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  pending: boolean;
  /** Reuses the same timer choices to set when a chat that is already paused goes back to the bot. */
  handBack?: boolean;
  onConfirm: (resumeAfterMinutes?: number) => void;
}) {
  // Matches what sending a manual reply already does, so the button and the composer agree.
  const [choice, setChoice] = useState(1);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>{handBack ? "Hand back to the bot later" : "Take over this chat"}</DialogTitle>
          <DialogDescription>
            {handBack
              ? "The bot stays off for now and takes the chat back on its own when the time is up."
              : "The bot stops replying here and picks up again on its own when the time is up."}
          </DialogDescription>
        </DialogHeader>

        <ToggleGroup
          type="single"
          variant="outline"
          value={String(choice)}
          onValueChange={(v) => v && setChoice(Number(v))}
          className="grid w-full grid-cols-2 gap-2"
        >
          {/* "Until I hand it back" is no use when the point is choosing a time to hand it back. */}
          {(handBack ? TAKEOVER_OPTIONS.slice(0, -1) : TAKEOVER_OPTIONS).map((option, index) => (
            <ToggleGroupItem
              key={option.label}
              value={String(index)}
              className={`h-auto w-full rounded-lg px-3 py-2 font-normal data-[state=on]:border-primary data-[state=on]:bg-primary/10 data-[state=on]:font-medium ${
                option.minutes === undefined ? "col-span-2" : ""
              }`}
            >
              {option.label}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            type="button"
            disabled={pending}
            onClick={() => onConfirm(TAKEOVER_OPTIONS[choice].minutes)}
          >
            {handBack ? "Set timer" : "Take over"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** "in about 40 minutes", or "shortly" once it is nearly due. */
function formatResume(iso: string): string {
  const minutes = Math.round((new Date(iso).getTime() - Date.now()) / 60_000);
  if (minutes <= 1) return "shortly";
  if (minutes < 60) return `in about ${minutes} minutes`;

  const hours = Math.round(minutes / 60);
  return `in about ${hours} hour${hours === 1 ? "" : "s"}`;
}
