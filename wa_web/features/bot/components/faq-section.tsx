"use client";

import { useState } from "react";
import { Pencil, Plus, Trash2, X, MessageSquareText } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
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
  useBotFaqs,
  useCreateBotFaq,
  useDeleteBotFaq,
  useUpdateBotFaq,
} from "@/features/bot/hooks/use-bot-profile";
import type { BotFaq } from "@/features/bot/types";

/**
 * The questions the business is tired of answering, and the answers it wants given.
 *
 * Every enabled entry goes into the assistant's prompt verbatim, so this is the fastest way to make
 * it right about something specific without touching the business description.
 */
export function FaqSection() {
  const { data: faqs, isLoading } = useBotFaqs();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [isAdding, setIsAdding] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<BotFaq | null>(null);

  const remove = useDeleteBotFaq();

  return (
    <SectionCard
      title="Prepared answers"
      icon={MessageSquareText}
      tone="teal"
      description={
        <>
          Questions you get all the time, answered the way you&apos;d answer them. The assistant
          uses these word for word.
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
      {isAdding ? <FaqEditor onDone={() => setIsAdding(false)} /> : null}

      {isLoading ? (
        <>
          <Skeleton className="h-20 w-full rounded-lg" />
          <Skeleton className="h-20 w-full rounded-lg" />
        </>
      ) : !faqs || faqs.length === 0 ? (
        !isAdding ? (
          <p className="rounded-lg border border-dashed px-3 py-8 text-center text-sm text-muted-foreground">
            Nothing here yet. Start with the three questions you answer most.
          </p>
        ) : null
      ) : (
        faqs.map((faq) =>
          editingId === faq.id ? (
            <FaqEditor key={faq.id} faq={faq} onDone={() => setEditingId(null)} />
          ) : (
            <div
              key={faq.id}
              className="flex items-start justify-between gap-4 rounded-lg border px-3 py-3"
            >
              <div className="min-w-0 space-y-1">
                <p
                  className={`text-sm font-medium ${faq.isEnabled ? "" : "text-muted-foreground line-through"}`}
                >
                  {faq.question}
                </p>
                <p className="whitespace-pre-wrap text-sm text-muted-foreground">{faq.answer}</p>
              </div>
              <div className="flex shrink-0 items-center gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    setEditingId(faq.id);
                    setIsAdding(false);
                  }}
                  aria-label="Edit answer"
                >
                  <Pencil className="size-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  onClick={() => setPendingDelete(faq)}
                  aria-label="Remove answer"
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
            <AlertDialogTitle>Remove this answer?</AlertDialogTitle>
            <AlertDialogDescription>
              The assistant will stop using it. Anything it has already said stays as it is.
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

/** Inline add/edit. Kept in the list rather than a dialog — these are short and often edited in runs. */
function FaqEditor({ faq, onDone }: { faq?: BotFaq; onDone: () => void }) {
  const create = useCreateBotFaq();
  const update = useUpdateBotFaq();

  const [question, setQuestion] = useState(faq?.question ?? "");
  const [answer, setAnswer] = useState(faq?.answer ?? "");
  const [isEnabled, setIsEnabled] = useState(faq?.isEnabled ?? true);

  const isPending = create.isPending || update.isPending;
  const canSave = question.trim().length > 0 && answer.trim().length > 0 && !isPending;

  function submit() {
    if (!canSave) return;

    const data = {
      question: question.trim(),
      answer: answer.trim(),
      sortOrder: faq?.sortOrder ?? 0,
      isEnabled,
    };

    if (faq) update.mutate({ id: faq.id, data }, { onSuccess: onDone });
    else create.mutate(data, { onSuccess: onDone });
  }

  return (
    <div className="space-y-3 rounded-lg border bg-muted/30 px-3 py-3">
      <div className="space-y-1.5">
        <Label>Question</Label>
        <Input
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Do you take card?"
          maxLength={500}
        />
      </div>

      <div className="space-y-1.5">
        <Label>Answer</Label>
        <Textarea
          value={answer}
          onChange={(e) => setAnswer(e.target.value)}
          rows={3}
          placeholder="Yes, all major cards, and cash if you prefer."
          maxLength={2000}
        />
      </div>

      <div className="flex items-center justify-between gap-3">
        <Label className="flex items-center gap-2 text-sm text-muted-foreground font-normal leading-normal">
          <Switch checked={isEnabled} onCheckedChange={setIsEnabled} />
          In use
        </Label>
        <div className="flex items-center gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={onDone}>
            <X className="mr-1 size-4" />
            Cancel
          </Button>
          <Button type="button" size="sm" onClick={submit} disabled={!canSave}>
            {isPending ? <Spinner className="mr-2 size-4" /> : null}
            {faq ? "Save" : "Add"}
          </Button>
        </div>
      </div>
    </div>
  );
}
