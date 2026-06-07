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
  useCreateWabaConnectionDialog,
  useEditWabaConnectionDialog,
  useActivateWabaConnectionDialog,
  useDeactivateWabaConnectionDialog,
} from "@/features/waba-connections/store/waba-connection-store";
import {
  useWabaConnection,
  useCreateWabaConnection,
  useUpdateWabaConnection,
  useActivateWabaConnection,
  useDeactivateWabaConnection,
} from "@/features/waba-connections/hooks/use-waba-connections";
import { WabaConnectionForm } from "./waba-connection-form";
import type {
  CreateWabaConnectionInput,
  UpdateWabaConnectionInput,
} from "@/features/waba-connections/schema/waba-connection-schema";

function CreateWabaConnectionDialog() {
  const { isOpen, close } = useCreateWabaConnectionDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateWabaConnection();

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
          <DialogTitle>Add WABA Connection</DialogTitle>
          <DialogDescription>
            Connect a WhatsApp Business phone number to a tenant company.
          </DialogDescription>
        </DialogHeader>
        <WabaConnectionForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateWabaConnectionInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditWabaConnectionDialog() {
  const { isOpen, selectedId, close } = useEditWabaConnectionDialog();
  const { data: conn, isLoading } = useWabaConnection(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateWabaConnection();

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
          <DialogTitle>Edit WABA Connection</DialogTitle>
          <DialogDescription>Update connection details.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <WabaConnectionForm
            mode="edit"
            defaultValues={
              conn
                ? {
                    companyId: conn.companyId,
                    phoneNumberId: conn.phoneNumberId,
                    wabaId: conn.wabaId,
                    displayPhoneNumber: conn.displayPhoneNumber ?? "",
                    accessToken: "",
                    status: conn.status,
                  }
                : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdateWabaConnectionInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ActivateWabaConnectionDialog() {
  const { isOpen, selectedId, close } = useActivateWabaConnectionDialog();
  const { mutate, isPending } = useActivateWabaConnection();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate connection?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the connection active and sets its status to Connected.
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

function DeactivateWabaConnectionDialog() {
  const { isOpen, selectedId, close } = useDeactivateWabaConnectionDialog();
  const { mutate, isPending } = useDeactivateWabaConnection();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate connection?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the connection inactive and sets its status to Disconnected. It can be
            reactivated later.
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

export function WabaConnectionDialogs() {
  return (
    <>
      <CreateWabaConnectionDialog />
      <EditWabaConnectionDialog />
      <ActivateWabaConnectionDialog />
      <DeactivateWabaConnectionDialog />
    </>
  );
}
