"use client";

import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import Link from "next/link";
import { toast } from "sonner";
import { Clock, FileText, Mic, Paperclip, SendHorizontal, Video, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import {
  useSendConversationMedia,
  useSendConversationMessage,
} from "@/features/conversations/hooks/use-conversations";
import {
  ATTACHMENT_ACCEPT,
  attachmentKind,
  formatFileSize,
  validateAttachment,
} from "@/features/conversations/attachments";

/** The picked file as a chip above the text box: a thumbnail for photos, an icon for everything else. */
function AttachmentChip({ file, onRemove, disabled }: { file: File; onRemove: () => void; disabled: boolean }) {
  const kind = attachmentKind(file.type);
  const previewUrl = useMemo(() => (kind === "image" ? URL.createObjectURL(file) : null), [file, kind]);
  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl); }, [previewUrl]);

  const Icon = kind === "audio" ? Mic : kind === "video" ? Video : FileText;

  return (
    <div className="flex items-center gap-3 rounded-xl bg-muted/60 p-2 pr-1.5">
      {previewUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- local object URL preview, not a remote image
        <img src={previewUrl} alt="" className="size-12 shrink-0 rounded-lg object-cover" />
      ) : (
        <span className="flex size-12 shrink-0 items-center justify-center rounded-lg bg-background text-muted-foreground">
          <Icon className="size-5" />
        </span>
      )}
      <div className="min-w-0 flex-1">
        <p className="truncate text-[13px] font-medium">{file.name}</p>
        <p className="text-[12px] text-muted-foreground">{formatFileSize(file.size)}</p>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        onClick={onRemove}
        disabled={disabled}
        className="size-8 shrink-0 rounded-full"
        aria-label="Remove attachment"
      >
        <X className="size-4" />
      </Button>
    </div>
  );
}

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
  const [file, setFile] = useState<File | null>(null);
  const send = useSendConversationMessage(conversationId);
  const sendMedia = useSendConversationMedia(conversationId);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const pending = send.isPending || sendMedia.isPending;
  const canSend = !pending && (file !== null || body.trim().length > 0);

  const pickFile = (picked: File | undefined) => {
    if (fileInputRef.current) fileInputRef.current.value = "";   // lets the same file be picked again
    if (!picked) return;
    const problem = validateAttachment(picked);
    if (problem) {
      toast.error(problem);
      return;
    }
    setFile(picked);
    textareaRef.current?.focus();
  };

  const submit = () => {
    if (!canSend) return;

    if (file) {
      sendMedia.mutate(
        { file, caption: body },
        {
          onSuccess: () => {
            setFile(null);
            setBody("");
            textareaRef.current?.focus();
          },
        },
      );
      return;
    }

    send.mutate(body.trim(), {
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
    <div className="flex flex-col gap-2 rounded-2xl bg-card p-2 shadow-[0_10px_30px_-18px_rgb(16_60_36/0.45)] ring-1 ring-black/5 focus-within:ring-primary/40">
      {file && <AttachmentChip file={file} onRemove={() => setFile(null)} disabled={pending} />}

      <div className="flex items-end gap-2">
        <Input
          ref={fileInputRef}
          type="file"
          accept={ATTACHMENT_ACCEPT.join(",")}
          className="hidden"
          tabIndex={-1}
          onChange={(e) => pickFile(e.target.files?.[0])}
        />
        <Button
          type="button"
          variant="ghost"
          size="icon"
          onClick={() => fileInputRef.current?.click()}
          disabled={pending}
          className="size-10 shrink-0 rounded-xl text-muted-foreground"
          aria-label="Attach a file"
        >
          <Paperclip className="size-4" />
        </Button>
        <Textarea
          ref={textareaRef}
          value={body}
          onChange={(e) => setBody(e.target.value)}
          onKeyDown={onKeyDown}
          rows={1}
          placeholder={file ? "Add a caption (optional)" : `Message ${contactName}`}
          aria-label={file ? "Caption" : "Message"}
          className="max-h-36 min-h-10 flex-1 resize-none border-0 bg-transparent px-2.5 py-2.5 text-[14px] shadow-none focus-visible:ring-0 md:text-[14px] dark:bg-transparent"
        />
        <Button
          type="button"
          size="icon"
          onClick={submit}
          disabled={!canSend}
          className="size-10 shrink-0 rounded-xl"
          aria-label={file ? "Send file" : "Send message"}
        >
          {pending ? <Spinner className="size-4" /> : <SendHorizontal className="size-4" />}
        </Button>
      </div>
    </div>
  );
}
