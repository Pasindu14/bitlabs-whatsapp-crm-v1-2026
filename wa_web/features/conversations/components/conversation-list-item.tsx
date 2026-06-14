"use client";

import { Check } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { cn } from "@/lib/utils";
import type { Conversation } from "@/features/conversations/types";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import { useMarkConversationRead } from "@/features/conversations/hooks/use-conversations";
import { initials, formatListTime } from "./format";

export function ConversationListItem({
  conversation,
  active,
}: {
  conversation: Conversation;
  active: boolean;
}) {
  const select = useConversationStore((s) => s.select);
  const markRead = useMarkConversationRead();

  const hasUnread = conversation.unreadCount > 0;
  const outbound = conversation.lastMessageDirection === "Outbound";
  const preview = conversation.lastMessageBody ?? "No messages yet";

  const onClick = () => {
    select(conversation);
    if (hasUnread) markRead.mutate(conversation.id);
  };

  return (
    <li>
      <button
        type="button"
        onClick={onClick}
        className={cn(
          "flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-muted/60",
          active && "bg-muted",
        )}
      >
        <Avatar className="size-10">
          <AvatarFallback className="bg-primary/10 font-medium text-primary">
            {initials(conversation.contactName)}
          </AvatarFallback>
        </Avatar>

        <div className="min-w-0 flex-1">
          <div className="flex items-center justify-between gap-2">
            <span className="truncate text-sm font-medium">{conversation.contactName}</span>
            <span
              className={cn(
                "shrink-0 text-xs",
                hasUnread ? "font-medium text-primary" : "text-muted-foreground",
              )}
            >
              {formatListTime(conversation.lastMessageAt)}
            </span>
          </div>

          <div className="mt-0.5 flex items-center justify-between gap-2">
            <span className="flex min-w-0 items-center gap-1 text-xs text-muted-foreground">
              {outbound && <Check className="size-3 shrink-0" />}
              <span className="truncate">{preview}</span>
            </span>
            {hasUnread && (
              <span className="flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-primary px-1.5 text-[10px] font-semibold text-primary-foreground">
                {conversation.unreadCount > 99 ? "99+" : conversation.unreadCount}
              </span>
            )}
          </div>
        </div>
      </button>
    </li>
  );
}
