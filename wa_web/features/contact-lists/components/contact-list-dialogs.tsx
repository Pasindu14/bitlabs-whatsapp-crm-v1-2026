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
  useCreateContactListDialog,
  useEditContactListDialog,
  useActivateContactListDialog,
  useDeactivateContactListDialog,
} from "@/features/contact-lists/store/contact-list-store";
import {
  useContactList,
  useCreateContactList,
  useUpdateContactList,
  useActivateContactList,
  useDeactivateContactList,
} from "@/features/contact-lists/hooks/use-contact-lists";
import { ContactListForm } from "./contact-list-form";
import { ManageContactsDialog } from "./manage-contacts-dialog";
import { ImportContactsDialog } from "./import-contacts-dialog";
import type {
  CreateContactListInput,
  UpdateContactListInput,
} from "@/features/contact-lists/schema/contact-list-schema";

function CreateContactListDialog() {
  const { isOpen, close } = useCreateContactListDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateContactList();

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
          <DialogTitle>Create Contact List</DialogTitle>
          <DialogDescription>
            Create a group you can add contacts to (e.g. for a campaign).
          </DialogDescription>
        </DialogHeader>
        <ContactListForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateContactListInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditContactListDialog() {
  const { isOpen, selectedId, close } = useEditContactListDialog();
  const { data: list, isLoading } = useContactList(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateContactList();

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
          <DialogTitle>Edit Contact List</DialogTitle>
          <DialogDescription>Update the list name or description.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <ContactListForm
            mode="edit"
            defaultValues={
              list ? { name: list.name, description: list.description ?? "" } : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdateContactListInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ActivateContactListDialog() {
  const { isOpen, selectedId, close } = useActivateContactListDialog();
  const { mutate, isPending } = useActivateContactList();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate list?</AlertDialogTitle>
          <AlertDialogDescription>This marks the list active again.</AlertDialogDescription>
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
            Activate
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function DeactivateContactListDialog() {
  const { isOpen, selectedId, close } = useDeactivateContactListDialog();
  const { mutate, isPending } = useDeactivateContactList();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate list?</AlertDialogTitle>
          <AlertDialogDescription>
            This hides the list. Its contacts are kept and it can be reactivated later.
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
            Deactivate
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

export function ContactListDialogs() {
  return (
    <>
      <CreateContactListDialog />
      <EditContactListDialog />
      <ActivateContactListDialog />
      <DeactivateContactListDialog />
      <ManageContactsDialog />
      <ImportContactsDialog />
    </>
  );
}
