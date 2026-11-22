"use client";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
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
import { Spinner } from "@/components/ui/spinner";
import {
  useCreateRuleDialog,
  useEditRuleDialog,
  useDeleteRuleDialog,
} from "@/features/bot/store/bot-store";
import {
  useAutoReplyRule,
  useCreateAutoReplyRule,
  useUpdateAutoReplyRule,
  useDeleteAutoReplyRule,
} from "@/features/bot/hooks/use-auto-reply-rules";
import { RuleForm } from "./rule-form";
import type {
  CreateAutoReplyRuleInput,
  UpdateAutoReplyRuleInput,
} from "@/features/bot/schema/bot-schema";

function CreateRuleDialog() {
  const { isOpen, close } = useCreateRuleDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateAutoReplyRule();

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          clearFieldErrors();
        }
      }}
    >
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Add Rule</DialogTitle>
          <DialogDescription>
            Choose what the customer has to say, and what the bot replies.
          </DialogDescription>
        </DialogHeader>
        <RuleForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateAutoReplyRuleInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditRuleDialog() {
  const { isOpen, selectedId, close } = useEditRuleDialog();
  const { data: rule, isLoading } = useAutoReplyRule(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateAutoReplyRule();

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          close();
          clearFieldErrors();
        }
      }}
    >
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Edit Rule</DialogTitle>
          <DialogDescription>Update this rule&apos;s trigger or reply.</DialogDescription>
        </DialogHeader>

        {isLoading || !rule ? (
          <div className="flex justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <RuleForm
            mode="edit"
            defaultValues={{
              name: rule.name,
              trigger: rule.trigger,
              terms: rule.terms,
              replyBody: rule.replyBody,
              priority: rule.priority,
              isEnabled: rule.isEnabled,
            }}
            onSubmit={(data) =>
              mutate({ id: rule.id, data: data as UpdateAutoReplyRuleInput })
            }
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function DeleteRuleDialog() {
  const { isOpen, selectedId, close } = useDeleteRuleDialog();
  const { mutate, isPending } = useDeleteAutoReplyRule();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete this rule?</AlertDialogTitle>
          <AlertDialogDescription>
            The bot will stop using it immediately. Conversations it already answered are
            unaffected.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              // Keep the dialog up while the request is in flight; the mutation closes it.
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
          >
            {isPending ? <Spinner className="mr-2 size-4" /> : null}
            Delete
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

export function RuleDialogs() {
  return (
    <>
      <CreateRuleDialog />
      <EditRuleDialog />
      <DeleteRuleDialog />
    </>
  );
}
