"use client";

import { useRef, useState, type KeyboardEvent } from "react";
import { Send } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { useSendConversationMessage } from "@/features/conversations/hooks/use-conversations";

export function MessageComposer({
  conversationId,
  windowOpen,
}: {
  conversationId: string;
  windowOpen: boolean;
}) {
  const [body, setBody] = useState("");
  const send = useSendConversationMessage(conversationId);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const submit = () => {
    const trimmed = body.trim();
    if (!trimmed || send.isPending) return;
    send.mutate(trimmed, {
      onSuccess: () => {
        setBody("");
        textareaRef.current?.focus();
      },
    });
  };

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  if (!windowOpen) {
    return (
      <div className="shrink-0 border-t bg-muted/40 px-4 py-4 text-center text-xs text-muted-foreground">
        The 24-hour customer service window has closed. You can reply again once the customer messages you.
      </div>
    );
  }

  return (
    <div className="shrink-0 border-t bg-card px-3 py-3">
      <div className="flex items-end gap-2">
        <Textarea
          ref={textareaRef}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          onKeyDown={onKeyDown}
          rows={1}
          placeholder="Type a message"
          className="max-h-32 min-h-10 flex-1 resize-none rounded-2xl py-2.5"
        />
        <Button
          type="button"
          size="icon"
          onClick={submit}
          disabled={!body.trim() || send.isPending}
          className="size-10 shrink-0 rounded-full"
          aria-label="Send message"
        >
          {send.isPending ? <Spinner className="size-4" /> : <Send className="size-4" />}
        </Button>
      </div>
    </div>
  );
}
