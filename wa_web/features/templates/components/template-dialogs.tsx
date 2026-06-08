"use client";

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
import { useSubmitTemplateDialog, useDeleteTemplateDialog } from "@/features/templates/store/template-store";
import { useSubmitTemplate, useDeleteTemplate } from "@/features/templates/hooks/use-templates";

function SubmitTemplateDialog() {
  const { isOpen, selectedId, selectedName, close } = useSubmitTemplateDialog();
  const { mutate, isPending } = useSubmitTemplate();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Submit “{selectedName}” to WhatsApp?</AlertDialogTitle>
          <AlertDialogDescription>
            This sends the template to Meta for review. Once submitted it can no longer be edited —
            to change it you would delete it and create a new template.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
          >
            {isPending && <Spinner className="mr-2" />}
            Submit
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function DeleteTemplateDialog() {
  const { isOpen, selectedId, selectedName, close } = useDeleteTemplateDialog();
  const { mutate, isPending } = useDeleteTemplate();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete “{selectedName}”?</AlertDialogTitle>
          <AlertDialogDescription>
            This removes the template from your workspace. If it was already submitted, it is also
            deleted from WhatsApp. This cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            {isPending && <Spinner className="mr-2" />}
            Delete
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

export function TemplateDialogs() {
  return (
    <>
      <SubmitTemplateDialog />
      <DeleteTemplateDialog />
    </>
  );
}
