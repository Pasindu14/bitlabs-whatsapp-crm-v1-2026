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
  useAssignSubscriptionDialog,
  useChangePlanDialog,
  useCancelSubscriptionDialog,
} from "@/features/subscriptions/store/subscription-store";
import {
  useAssignSubscription,
  useChangePlan,
  useCancelSubscription,
} from "@/features/subscriptions/hooks/use-subscriptions";
import { AssignSubscriptionForm } from "./assign-subscription-form";
import { ChangePlanForm } from "./change-plan-form";

function AssignSubscriptionDialog() {
  const { isOpen, close } = useAssignSubscriptionDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useAssignSubscription();

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
          <DialogTitle>Assign Subscription</DialogTitle>
          <DialogDescription>
            Place a company on a plan. Any existing active subscription is replaced.
          </DialogDescription>
        </DialogHeader>
        <AssignSubscriptionForm
          onSubmit={(data) => mutate(data)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function ChangePlanDialog() {
  const { isOpen, selectedCompanyId, close } = useChangePlanDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useChangePlan();

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
          <DialogTitle>Change Plan</DialogTitle>
          <DialogDescription>Move this company to a different plan.</DialogDescription>
        </DialogHeader>
        <ChangePlanForm
          onSubmit={(data) => {
            if (!selectedCompanyId) return;
            mutate({ companyId: selectedCompanyId, data });
          }}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function CancelSubscriptionDialog() {
  const { isOpen, selectedCompanyId, close } = useCancelSubscriptionDialog();
  const { mutate, isPending } = useCancelSubscription();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Cancel subscription?</AlertDialogTitle>
          <AlertDialogDescription>
            This cancels the company&apos;s active subscription. They will be blocked from sending
            until a new plan is assigned.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Keep</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedCompanyId) mutate(selectedCompanyId);
            }}
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
          >
            {isPending && <Spinner className="mr-2" />}
            Cancel subscription
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

export function SubscriptionDialogs() {
  return (
    <>
      <AssignSubscriptionDialog />
      <ChangePlanDialog />
      <CancelSubscriptionDialog />
    </>
  );
}
