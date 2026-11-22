"use client";

import { Fragment, useEffect, useRef, useState } from "react";
import { ArrowLeft, Bot, MessageCircle, User } from "lucide-react";
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
import { initials, formatDayDivider, sameDay, displayPhone } from "./format";

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
      <div className="flex h-full flex-col items-center justify-center gap-3 bg-muted/20 px-6 text-center">
        <div className="flex size-14 items-center justify-center rounded-full bg-primary/10">
          <MessageCircle className="size-7 text-primary" />
        </div>
        <p className="text-sm font-medium">Select a conversation</p>
        <p className="max-w-xs text-xs text-muted-foreground">
          Choose a conversation from the list to view the thread and reply.
        </p>
      </div>
    );
  }

  const windowOpen = selected.windowExpiresAt ? new Date(selected.windowExpiresAt) > new Date() : false;

  return (
    <div className="flex h-full flex-col bg-muted/20">
      <div className="flex shrink-0 items-center gap-3 border-b bg-card px-3 py-2.5 sm:px-4">
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={() => select(null)}
          aria-label="Back to inbox"
        >
          <ArrowLeft className="size-5" />
        </Button>
        <Avatar className="size-9">
          <AvatarFallback className="bg-primary/10 text-xs font-medium text-primary">
            {initials(selected.contactName)}
          </AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold">{selected.contactName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {displayPhone(selected.contactPhone)} · via {selected.displayPhoneNumber}
          </p>
        </div>

        {botTyping ? <BotWorkingBadge /> : null}
      </div>

      <ScrollArea className="min-h-0 flex-1 px-3 sm:px-4">
        {isLoading ? (
          <div className="flex h-40 items-center justify-center">
            <Spinner className="size-5 text-muted-foreground" />
          </div>
        ) : !messages || messages.length === 0 ? (
          <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">
            No messages yet.
          </div>
        ) : (
          <div className="flex flex-col gap-1.5 py-4">
            {messages.map((m, i) => {
              const showDivider = i === 0 || !sameDay(messages[i - 1].createdAt, m.createdAt);
              return (
                <Fragment key={m.id}>
                  {showDivider && (
                    <div className="my-2 flex justify-center">
                      <span className="rounded-full bg-muted px-3 py-1 text-[11px] font-medium text-muted-foreground">
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
      </ScrollArea>

      {botAccess.chatbot && botAccess.platformEnabled ? (
        <BotStateBanner
          conversationId={selected.id}
          botState={selected.botState}
          botResumesAt={selected.botResumesAt}
          botPausedByAgent={selected.botPausedByAgent}
        />
      ) : null}

      <MessageComposer conversationId={selected.id} windowOpen={windowOpen} />
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
    <div className="flex items-center justify-between gap-3 border-t bg-muted/40 px-4 py-2 text-xs">
      <span className="flex items-center gap-2 text-muted-foreground">
        {paused ? (
          <>
            <User className="size-3.5" />
            {handedOver ? "The bot passed this chat to your team" : "You're handling this chat"}
            {botResumesAt
              ? ` — the bot takes over again ${formatResume(botResumesAt)}`
              : " — the bot stays off until you hand it back"}
          </>
        ) : (
          <>
            <Bot className="size-3.5" />
            The bot is handling this chat
          </>
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
          variant="ghost"
          size="sm"
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

        <div className="grid grid-cols-2 gap-2">
          {/* "Until I hand it back" is no use when the point is choosing a time to hand it back. */}
          {(handBack ? TAKEOVER_OPTIONS.slice(0, -1) : TAKEOVER_OPTIONS).map((option, index) => (
            <button
              key={option.label}
              type="button"
              onClick={() => setChoice(index)}
              className={`rounded-lg border px-3 py-2 text-sm transition-colors ${
                choice === index
                  ? "border-primary bg-primary/10 font-medium"
                  : "hover:bg-muted"
              } ${option.minutes === undefined ? "col-span-2" : ""}`}
            >
              {option.label}
            </button>
          ))}
        </div>

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
