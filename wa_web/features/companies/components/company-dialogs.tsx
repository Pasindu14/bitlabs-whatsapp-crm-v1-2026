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
  useCreateCompanyDialog,
  useEditCompanyDialog,
  useActivateCompanyDialog,
  useDeactivateCompanyDialog,
} from "@/features/companies/store/company-store";
import {
  useCompany,
  useCreateCompany,
  useUpdateCompany,
  useActivateCompany,
  useDeactivateCompany,
} from "@/features/companies/hooks/use-companies";
import { CompanyForm } from "./company-form";
import type {
  CreateCompanyInput,
  UpdateCompanyInput,
} from "@/features/companies/schema/company-schema";

function CreateCompanyDialog() {
  const { isOpen, close } = useCreateCompanyDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateCompany();

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
          <DialogTitle>Add Company</DialogTitle>
          <DialogDescription>
            Create a new tenant. Only Super Admins can create companies.
          </DialogDescription>
        </DialogHeader>
        <CompanyForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateCompanyInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditCompanyDialog() {
  const { isOpen, selectedId, close } = useEditCompanyDialog();
  const { data: company, isLoading } = useCompany(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateCompany();

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
          <DialogTitle>Edit Company</DialogTitle>
          <DialogDescription>Update company information.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <CompanyForm
            mode="edit"
            defaultValues={
              company
                ? {
                    name: company.name,
                    slug: company.slug ?? "",
                    email: company.email ?? "",
                    phone: company.phone ?? "",
                  }
                : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdateCompanyInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ActivateCompanyDialog() {
  const { isOpen, selectedId, close } = useActivateCompanyDialog();
  const { mutate, isPending } = useActivateCompany();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate company?</AlertDialogTitle>
          <AlertDialogDescription>
            This will mark the company as active and visible among active tenants.
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

function DeactivateCompanyDialog() {
  const { isOpen, selectedId, close } = useDeactivateCompanyDialog();
  const { mutate, isPending } = useDeactivateCompany();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate company?</AlertDialogTitle>
          <AlertDialogDescription>
            This will mark the company as inactive. It can be reactivated later.
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

export function CompanyDialogs() {
  return (
    <>
      <CreateCompanyDialog />
      <EditCompanyDialog />
      <ActivateCompanyDialog />
      <DeactivateCompanyDialog />
    </>
  );
}
