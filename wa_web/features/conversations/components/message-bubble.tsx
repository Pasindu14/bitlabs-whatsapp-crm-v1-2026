"use client";

import { useState } from "react";
import {
  Check,
  CheckCheck,
  Clock,
  Download,
  FileText,
  Image as ImageIcon,
  Mic,
  TriangleAlert,
  Video,
} from "lucide-react";
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

/** Human label + icon for a non-image media type (image is rendered inline, not chipped). */
function mediaMeta(mediaType: string) {
  switch (mediaType) {
    case "video":
      return { label: "Video", Icon: Video };
    case "audio":
      return { label: "Audio message", Icon: Mic };
    case "document":
      return { label: "Document", Icon: FileText };
    default:
      return { label: "Attachment", Icon: FileText };
  }
}

/** Renders the media portion of a message (image inline, other types as a chip); null for plain text. */
function MessageMedia({ message }: { message: ConversationMessage }) {
  const [imgError, setImgError] = useState(false);
  const mediaType = message.mediaType;
  if (!mediaType) return null;

  const url = `/api/media/${message.id}`;
  const isImage = mediaType === "image" || mediaType === "sticker";

  // Bytes not yet downloaded from Meta → placeholder until the realtime "media ready" event arrives.
  if (!message.mediaReady) {
    const { Icon, label } = isImage ? { Icon: ImageIcon, label: "Photo" } : mediaMeta(mediaType);
    return (
      <div className="flex items-center gap-2 rounded-lg bg-background/40 px-3 py-6 text-muted-foreground">
        <Icon className="size-5 animate-pulse" />
        <span className="text-xs">{label}…</span>
      </div>
    );
  }

  if (isImage && !imgError) {
    return (
      <a href={url} target="_blank" rel="noopener noreferrer" className="block">
        {/* eslint-disable-next-line @next/next/no-img-element -- served via same-origin proxy, not next/image */}
        <img
          src={url}
          alt={message.body || "Image"}
          loading="lazy"
          onError={() => setImgError(true)}
          className="max-h-80 w-auto max-w-full rounded-lg object-cover"
        />
      </a>
    );
  }

  // Non-image media, or an image that failed to load → a downloadable attachment chip.
  const { label, Icon } = isImage ? { label: "Image", Icon: ImageIcon } : mediaMeta(mediaType);
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      className="flex items-center gap-2 rounded-lg border bg-background/50 px-3 py-2 transition-colors hover:bg-background/80"
    >
      <Icon className="size-5 shrink-0" />
      <span className="flex-1 truncate text-sm">{label}</span>
      <Download className="size-4 shrink-0 opacity-60" />
    </a>
  );
}

export function MessageBubble({ message }: { message: ConversationMessage }) {
  const outbound = message.direction === "Outbound";
  const failed = outbound && message.status === "Failed";

  const hasMedia = !!message.mediaType;
  // The backend stores "[image]"/"[video]"/… as the body when a media message has no caption; don't show it
  // as text under the media — only render the body when it's a real caption (or a plain text message).
  const isPlaceholderBody = hasMedia && message.body === `[${message.mediaType}]`;
  const showBody = message.body.length > 0 && !isPlaceholderBody;

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
        {hasMedia && (
          <div className={cn(showBody && "mb-1.5")}>
            <MessageMedia message={message} />
          </div>
        )}

        {showBody && (
          <p className="whitespace-pre-wrap break-words">{renderWhatsAppText(message.body)}</p>
        )}

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
