"use client";

import { Bot, Check, Hand } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { cn } from "@/lib/utils";
import type { Conversation } from "@/features/conversations/types";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import { useMarkConversationRead } from "@/features/conversations/hooks/use-conversations";
import { initials, formatListTime, avatarTone } from "./format";
import { Button } from "@/components/ui/button";

export function ConversationListItem({
  conversation,
  active,
  botOn,
}: {
  conversation: Conversation;
  active: boolean;
  /** The company has the chatbot, so show who is answering each chat. */
  botOn: boolean;
}) {
  const select = useConversationStore((s) => s.select);
  const markRead = useMarkConversationRead();

  const hasUnread = conversation.unreadCount > 0;
  const outbound = conversation.lastMessageDirection === "Outbound";
  const preview = conversation.lastMessageBody ?? "No messages yet";
  const handedOver = conversation.botState === "Paused" && !conversation.botPausedByAgent;
  const name = conversation.contactName || conversation.contactPhone;

  const onClick = () => {
    select(conversation);
    if (hasUnread) markRead.mutate(conversation.id);
  };

  return (
    <li className="min-w-0">
      <Button
        type="button"
        variant="ghost"
        onClick={onClick}
        aria-current={active ? "true" : undefined}
        className={cn(
          "relative flex h-auto w-full min-w-0 items-center justify-start gap-3 rounded-xl px-3 py-2.5 text-left font-normal whitespace-normal hover:bg-muted/70 active:translate-y-0",
          active && "bg-[#e6f2ea] hover:bg-[#e6f2ea] dark:bg-primary/15 dark:hover:bg-primary/15",
        )}
      >
        <Avatar className="size-11">
          <AvatarFallback className={cn("text-sm font-medium", avatarTone(name))}>{initials(name)}</AvatarFallback>
        </Avatar>

        <div className="min-w-0 flex-1">
          <div className="flex items-center justify-between gap-2">
            <span className={cn("truncate text-[14px]", hasUnread ? "font-semibold" : "font-medium")}>{name}</span>
            <span
              className={cn(
                "shrink-0 text-[11px] tabular-nums",
                hasUnread ? "font-semibold text-primary" : "text-muted-foreground",
              )}
            >
              {formatListTime(conversation.lastMessageAt)}
            </span>
          </div>

          <div className="mt-0.5 flex items-center justify-between gap-2">
            <span
              className={cn(
                "flex min-w-0 items-center gap-1 text-[13px]",
                hasUnread ? "text-foreground" : "text-muted-foreground",
              )}
            >
              {outbound && <Check className="size-3.5 shrink-0" />}
              <span className="truncate">{preview}</span>
            </span>

            <span className="flex shrink-0 items-center gap-1.5">
              {botOn && handedOver && (
                <span title="The bot handed this chat to your team" className="text-amber-600">
                  <Hand className="size-3.5" />
                </span>
              )}
              {botOn && conversation.botState === "Active" && (
                <span title="The bot is answering this chat" className="text-muted-foreground/70">
                  <Bot className="size-3.5" />
                </span>
              )}
              {hasUnread && (
                <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-[10px] font-semibold text-primary-foreground tabular-nums">
                  {conversation.unreadCount > 99 ? "99+" : conversation.unreadCount}
                </span>
              )}
            </span>
          </div>
        </div>
      </Button>
    </li>
  );
}
