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
  useCreatePlanDialog,
  useEditPlanDialog,
  useActivatePlanDialog,
  useDeactivatePlanDialog,
} from "@/features/plans/store/plan-store";
import {
  usePlan,
  useCreatePlan,
  useUpdatePlan,
  useActivatePlan,
  useDeactivatePlan,
} from "@/features/plans/hooks/use-plans";
import { PlanForm } from "./plan-form";
import type {
  CreatePlanInput,
  UpdatePlanInput,
} from "@/features/plans/schema/plan-schema";
import type { PermissionKey } from "@/features/team/permissions";

function CreatePlanDialog() {
  const { isOpen, close } = useCreatePlanDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreatePlan();

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
          <DialogTitle>Add Plan</DialogTitle>
          <DialogDescription>
            Define a subscription tier — quota, price, and the features it unlocks.
          </DialogDescription>
        </DialogHeader>
        <PlanForm
          mode="create"
          onSubmit={(data) => mutate(data as CreatePlanInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditPlanDialog() {
  const { isOpen, selectedId, close } = useEditPlanDialog();
  const { data: plan, isLoading } = usePlan(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdatePlan();

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
          <DialogTitle>Edit Plan</DialogTitle>
          <DialogDescription>Update plan details.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <PlanForm
            mode="edit"
            defaultValues={
              plan
                ? {
                    name: plan.name,
                    monthlyMessageQuota: plan.monthlyMessageQuota,
                    price: plan.price,
                    currency: plan.currency,
                    featureFlags: plan.featureFlags as PermissionKey[],
                    isOnline: plan.isOnline,
                  }
                : undefined
            }
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdatePlanInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ActivatePlanDialog() {
  const { isOpen, selectedId, close } = useActivatePlanDialog();
  const { mutate, isPending } = useActivatePlan();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Activate plan?</AlertDialogTitle>
          <AlertDialogDescription>
            This marks the plan active so it can be assigned to companies.
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

function DeactivatePlanDialog() {
  const { isOpen, selectedId, close } = useDeactivatePlanDialog();
  const { mutate, isPending } = useDeactivatePlan();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Deactivate plan?</AlertDialogTitle>
          <AlertDialogDescription>
            This hides the plan from new assignments. Existing subscriptions are unaffected. It can
            be reactivated later.
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

export function PlanDialogs() {
  return (
    <>
      <CreatePlanDialog />
      <EditPlanDialog />
      <ActivatePlanDialog />
      <DeactivatePlanDialog />
    </>
  );
}
