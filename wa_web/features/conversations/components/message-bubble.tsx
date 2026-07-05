"use client";

import { Check, CheckCheck, Clock, TriangleAlert } from "lucide-react";
import { cn } from "@/lib/utils";
import { renderWhatsAppText } from "@/lib/whatsapp-text";
import type { ConversationMessage, MessageStatus } from "@/features/conversations/types";
import { formatMessageTime } from "./format";

function StatusTick({ status }: { status: MessageStatus }) {
  switch (status) {
    case "Accepted":
      // Queued at Meta, awaiting the 'sent' status webhook — pending clock, no tick yet.
      return <Clock className="size-3.5" />;
    case "Sent":
      return <Check className="size-3.5" />;
    case "Delivered":
      return <CheckCheck className="size-3.5" />;
    case "Read":
      return <CheckCheck className="size-3.5 text-sky-400" />;
    case "Failed":
      return <TriangleAlert className="size-3.5 text-destructive" />;
    default:
      return <Clock className="size-3.5" />;
  }
}

export function MessageBubble({ message }: { message: ConversationMessage }) {
  const outbound = message.direction === "Outbound";
  const failed = outbound && message.status === "Failed";

  return (
    <div className={cn("flex w-full", outbound ? "justify-end" : "justify-start")}>
      <div
        className={cn(
          "max-w-[78%] rounded-2xl px-3 py-2 text-sm shadow-sm sm:max-w-[70%]",
          outbound
            ? "rounded-br-sm bg-primary text-primary-foreground"
            : "rounded-bl-sm border bg-card text-card-foreground",
          failed && "ring-1 ring-destructive/40",
        )}
      >
        <p className="whitespace-pre-wrap break-words">{renderWhatsAppText(message.body)}</p>

        {failed && message.errorMessage && (
          <p className="mt-1 text-[11px] text-primary-foreground/80">{message.errorMessage}</p>
        )}

        <div
          className={cn(
            "mt-1 flex items-center justify-end gap-1 text-[10px]",
            outbound ? "text-primary-foreground/70" : "text-muted-foreground",
          )}
        >
          <span>{formatMessageTime(message.createdAt)}</span>
          {outbound && <StatusTick status={message.status} />}
        </div>
      </div>
    </div>
  );
}
