"use client";

import { useState } from "react";
import { ExternalLink, Pencil, Plus, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import { SectionCard } from "./section-card";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  useBotLinks,
  useCreateBotLink,
  useDeleteBotLink,
  useUpdateBotLink,
} from "@/features/bot/hooks/use-bot-profile";
import type { BotLink } from "@/features/bot/types";

const URL_PATTERN = /^https?:\/\/\S+$/i;

/**
 * Links the assistant may send — a before/after gallery, a price list, a map pin.
 *
 * The assistant only ever sees the title and the "when to share" hint; the URL itself is sent by the
 * server exactly as saved here, so it can never be mistyped or invented.
 */
export function LinkSection() {
  const { data: links, isLoading } = useBotLinks();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [isAdding, setIsAdding] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<BotLink | null>(null);

  const remove = useDeleteBotLink();

  return (
    <SectionCard
      title="Links to share"
      description={
        <>
          Photos, price lists, maps — anything the assistant can send when a customer asks. It sends
          the link exactly as you save it.
        </>
      }
      actions={
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => {
            setIsAdding(true);
            setEditingId(null);
          }}
        >
          <Plus className="mr-2 size-4" />
          Add
        </Button>
      }
      contentClassName="space-y-3"
    >
      {isAdding ? <LinkEditor onDone={() => setIsAdding(false)} /> : null}

      {isLoading ? (
        <>
          <Skeleton className="h-16 w-full rounded-lg" />
          <Skeleton className="h-16 w-full rounded-lg" />
        </>
      ) : !links || links.length === 0 ? (
        !isAdding ? (
          <p className="rounded-lg border border-dashed px-3 py-8 text-center text-sm text-muted-foreground">
            No links yet. Add your before &amp; after photos so the assistant can share them.
          </p>
        ) : null
      ) : (
        links.map((link) =>
          editingId === link.id ? (
            <LinkEditor key={link.id} link={link} onDone={() => setEditingId(null)} />
          ) : (
            <div
              key={link.id}
              className="flex items-start justify-between gap-4 rounded-lg border px-3 py-3"
            >
              <div className="min-w-0 space-y-1">
                <p
                  className={`text-sm font-medium ${link.isEnabled ? "" : "text-muted-foreground line-through"}`}
                >
                  {link.title}
                </p>
                <a
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-1 truncate text-xs text-primary hover:underline"
                >
                  <ExternalLink className="size-3 shrink-0" />
                  <span className="truncate">{link.url}</span>
                </a>
                {link.whenToShare ? (
                  <p className="text-sm text-muted-foreground">
                    Shared {lowerFirst(link.whenToShare)}
                  </p>
                ) : null}
              </div>
              <div className="flex shrink-0 items-center gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    setEditingId(link.id);
                    setIsAdding(false);
                  }}
                  aria-label="Edit link"
                >
                  <Pencil className="size-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  onClick={() => setPendingDelete(link)}
                  aria-label="Remove link"
                >
                  <Trash2 className="size-4" />
                </Button>
              </div>
            </div>
          ),
        )
      )}

      <AlertDialog open={!!pendingDelete} onOpenChange={(open) => !open && setPendingDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove this link?</AlertDialogTitle>
            <AlertDialogDescription>
              The assistant will stop sharing it. Messages it has already sent stay as they are.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                // The mutation closes the dialog on success, so the default close is prevented.
                e.preventDefault();
                if (!pendingDelete) return;
                remove.mutate(pendingDelete.id, {
                  onSuccess: () => setPendingDelete(null),
                });
              }}
            >
              {remove.isPending ? <Spinner className="mr-2 size-4" /> : null}
              Remove
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </SectionCard>
  );
}

/** Inline add/edit, same as prepared answers. */
function LinkEditor({ link, onDone }: { link?: BotLink; onDone: () => void }) {
  const create = useCreateBotLink();
  const update = useUpdateBotLink();

  const [title, setTitle] = useState(link?.title ?? "");
  const [url, setUrl] = useState(link?.url ?? "");
  const [whenToShare, setWhenToShare] = useState(link?.whenToShare ?? "");
  const [message, setMessage] = useState(link?.message ?? "");
  const [isEnabled, setIsEnabled] = useState(link?.isEnabled ?? true);

  const isPending = create.isPending || update.isPending;
  const urlValid = URL_PATTERN.test(url.trim());
  const canSave = title.trim().length > 0 && urlValid && !isPending;

  function submit() {
    if (!canSave) return;

    const data = {
      title: title.trim(),
      url: url.trim(),
      whenToShare: whenToShare.trim() || null,
      message: message.trim() || null,
      sortOrder: link?.sortOrder ?? 0,
      isEnabled,
    };

    if (link) update.mutate({ id: link.id, data }, { onSuccess: onDone });
    else create.mutate(data, { onSuccess: onDone });
  }

  return (
    <div className="space-y-3 rounded-lg border bg-muted/30 px-3 py-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>Title</Label>
          <Input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Before & after photos"
            maxLength={100}
          />
        </div>

        <div className="space-y-1.5">
          <Label>Link</Label>
          <Input
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://..."
            maxLength={2000}
            inputMode="url"
            aria-invalid={url.length > 0 && !urlValid}
          />
          {url.length > 0 && !urlValid ? (
            <p className="text-xs text-destructive">Enter a full link starting with https://</p>
          ) : null}
        </div>
      </div>

      <div className="space-y-1.5">
        <Label>When to share it</Label>
        <Input
          value={whenToShare}
          onChange={(e) => setWhenToShare(e.target.value)}
          placeholder="When they ask to see results, photos, or before and after"
          maxLength={500}
        />
      </div>

      <div className="space-y-1.5">
        <Label>
          Message with the link{" "}
          <span className="font-normal text-muted-foreground">(optional)</span>
        </Label>
        <Input
          value={message}
          onChange={(e) => setMessage(e.target.value)}
          placeholder="Here are some real results from our clients 👇"
          maxLength={300}
        />
      </div>

      <div className="flex items-center justify-between gap-3">
        <label className="flex items-center gap-2 text-sm text-muted-foreground">
          <Switch checked={isEnabled} onCheckedChange={setIsEnabled} />
          In use
        </label>
        <div className="flex items-center gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={onDone}>
            <X className="mr-1 size-4" />
            Cancel
          </Button>
          <Button type="button" size="sm" onClick={submit} disabled={!canSave}>
            {isPending ? <Spinner className="mr-2 size-4" /> : null}
            {link ? "Save" : "Add"}
          </Button>
        </div>
      </div>
    </div>
  );
}

function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1);
}
