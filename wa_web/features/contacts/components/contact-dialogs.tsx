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
  useCreateContactDialog,
  useEditContactDialog,
  useActivateContactDialog,
  useDeactivateContactDialog,
} from "@/features/contacts/store/contact-store";
import {
  useContact,
  useCreateContact,
  useUpdateContact,
  useActivateContact,
  useDeactivateContact,
} from "@/features/contacts/hooks/use-contacts";
import { ContactForm } from "./contact-form";
import type {
  CreateContactInput,
  UpdateContactInput,
} from "@/features/contacts/schema/contact-schema";

function CreateContactDialog() {
  const { isOpen, close } = useCreateContactDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateContact();

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
          <DialogTitle>Add Contact</DialogTitle>
          <DialogDescription>Add a person your company can message on WhatsApp.</DialogDescription>
        </DialogHeader>
        <ContactForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateContactInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditContactDialog() {
  const { isOpen, selectedId, close } = useEditContactDialog();
  const { data: contact, isLoading } = useContact(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateContact();

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
          <DialogTitle>Edit Contact</DialogTitle>
          <DialogDescription>Update this contact&apos;s name or phone number.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <ContactForm
            mode="edit"
            defaultValues={
              contact
                ? { name: contact.name, phone: contact.phone, hasOptedIn: contact.hasOptedIn }
                : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdateContactInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ActivateContactDialog() {
  const { isOpen, selectedId, close } = useActivateContactDialog();
  const { mutate, isPending } = useActivateContact();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate contact?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the contact active so it appears in your messaging lists again.
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
            Activate
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function DeactivateContactDialog() {
  const { isOpen, selectedId, close } = useDeactivateContactDialog();
  const { mutate, isPending } = useDeactivateContact();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate contact?</AlertDialogTitle>
          <AlertDialogDescription>
            This hides the contact from your messaging lists. It can be reactivated later.
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

export function ContactDialogs() {
  return (
    <>
      <CreateContactDialog />
      <EditContactDialog />
      <ActivateContactDialog />
      <DeactivateContactDialog />
    </>
  );
}
