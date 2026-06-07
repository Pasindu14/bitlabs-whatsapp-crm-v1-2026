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
import { useDeleteCompany } from "@/features/companies/hooks/use-companies";
import { useCompanyStore } from "@/features/companies/store/company-store";

export function DeleteCompanyDialog() {
  const target = useCompanyStore((s) => s.deleteTarget);
  const setTarget = useCompanyStore((s) => s.setDeleteTarget);

  const { mutate, isPending } = useDeleteCompany(() => setTarget(null));

  return (
    <AlertDialog open={!!target} onOpenChange={(open) => !open && setTarget(null)}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate company?</AlertDialogTitle>
          <AlertDialogDescription>
            {target ? (
              <>
                <span className="font-medium text-foreground">{target.name}</span> will be
                deactivated and hidden from active tenants. This can be reversed later.
              </>
            ) : null}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (target) mutate(target.id);
            }}
          >
            Deactivate
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
