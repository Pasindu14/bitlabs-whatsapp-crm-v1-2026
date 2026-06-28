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
  useCreatePackageDialog,
  useEditPackageDialog,
  useActivatePackageDialog,
  useDeactivatePackageDialog,
} from "@/features/packages/store/package-store";
import {
  usePackage,
  useCreatePackage,
  useUpdatePackage,
  useActivatePackage,
  useDeactivatePackage,
} from "@/features/packages/hooks/use-packages";
import { PackageForm } from "./package-form";
import type {
  CreatePackageInput,
  UpdatePackageInput,
} from "@/features/packages/schema/package-schema";

function CreatePackageDialog() {
  const { isOpen, close } = useCreatePackageDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreatePackage();

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
          <DialogTitle>Add Package</DialogTitle>
          <DialogDescription>
            Define an extra-credit bundle companies can add on top of their plan quota.
          </DialogDescription>
        </DialogHeader>
        <PackageForm
          mode="create"
          onSubmit={(data) => mutate(data as CreatePackageInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditPackageDialog() {
  const { isOpen, selectedId, close } = useEditPackageDialog();
  const { data: pkg, isLoading } = usePackage(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdatePackage();

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
          <DialogTitle>Edit Package</DialogTitle>
          <DialogDescription>Update package details.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <PackageForm
            mode="edit"
            defaultValues={
              pkg
                ? {
                    name: pkg.name,
                    description: pkg.description ?? undefined,
                    extraMessages: pkg.extraMessages,
                    price: pkg.price,
                    currency: pkg.currency,
                  }
                : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdatePackageInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ActivatePackageDialog() {
  const { isOpen, selectedId, close } = useActivatePackageDialog();
  const { mutate, isPending } = useActivatePackage();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate package?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the package active so it can be added to companies.
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

function DeactivatePackageDialog() {
  const { isOpen, selectedId, close } = useDeactivatePackageDialog();
  const { mutate, isPending } = useDeactivatePackage();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate package?</AlertDialogTitle>
          <AlertDialogDescription>
            This hides the package from new add-ons. Credits already granted to companies are
            unaffected. It can be reactivated later.
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

export function PackageDialogs() {
  return (
    <>
      <CreatePackageDialog />
      <EditPackageDialog />
      <ActivatePackageDialog />
      <DeactivatePackageDialog />
    </>
  );
}
