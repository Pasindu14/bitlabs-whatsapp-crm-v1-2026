"use client";

import { useRef, useState, type KeyboardEvent } from "react";
import Link from "next/link";
import { Clock, SendHorizontal } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { useSendConversationMessage } from "@/features/conversations/hooks/use-conversations";

export function MessageComposer({
  conversationId,
  windowOpen,
  contactName,
}: {
  conversationId: string;
  windowOpen: boolean;
  contactName: string;
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
      <div className="flex items-center gap-3 rounded-2xl bg-card px-4 py-3 text-[13px] ring-1 ring-black/5">
        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
          <Clock className="size-4" />
        </span>
        <p className="min-w-0 flex-1 leading-relaxed text-muted-foreground">
          The 24-hour reply window has closed. You can reply once {contactName} messages again, or reach
          them now with an approved template.
        </p>
        <Button asChild variant="outline" size="sm" className="shrink-0 rounded-full">
          <Link href="/campaigns">Send a template</Link>
        </Button>
      </div>
    );
  }

  return (
    <div className="flex items-end gap-2 rounded-2xl bg-card p-2 shadow-[0_10px_30px_-18px_rgb(16_60_36/0.45)] ring-1 ring-black/5 focus-within:ring-primary/40">
      <Textarea
        ref={textareaRef}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        onKeyDown={onKeyDown}
        rows={1}
        placeholder={`Message ${contactName}`}
        aria-label="Message"
        className="max-h-36 min-h-10 flex-1 resize-none border-0 bg-transparent px-2.5 py-2.5 text-[14px] shadow-none focus-visible:ring-0 md:text-[14px] dark:bg-transparent"
      />
      <Button
        type="button"
        size="icon"
        onClick={submit}
        disabled={!body.trim() || send.isPending}
        className="size-10 shrink-0 rounded-xl"
        aria-label="Send message"
      >
        {send.isPending ? <Spinner className="size-4" /> : <SendHorizontal className="size-4" />}
      </Button>
    </div>
  );
}
