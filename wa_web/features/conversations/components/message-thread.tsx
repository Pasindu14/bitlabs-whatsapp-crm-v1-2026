"use client";

import { Fragment, useEffect, useRef } from "react";
import { ArrowLeft, MessageCircle } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Spinner } from "@/components/ui/spinner";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import { useConversationMessages } from "@/features/conversations/hooks/use-conversations";
import { MessageBubble } from "./message-bubble";
import { MessageComposer } from "./message-composer";
import { initials, formatDayDivider, sameDay, displayPhone } from "./format";

export function MessageThread() {
  const selected = useConversationStore((s) => s.selected);
  const select = useConversationStore((s) => s.select);
  const { data: messages, isLoading } = useConversationMessages(selected?.id ?? null);
  const bottomRef = useRef<HTMLDivElement>(null);

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
                  <MessageBubble message={m} />
                </Fragment>
              );
            })}
            <div ref={bottomRef} />
          </div>
        )}
      </ScrollArea>

      <MessageComposer conversationId={selected.id} windowOpen={windowOpen} />
    </div>
  );
}
