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
  useAddPackageDialog,
} from "@/features/subscriptions/store/subscription-store";
import {
  useAssignSubscription,
  useChangePlan,
  useCancelSubscription,
  useAddPackage,
} from "@/features/subscriptions/hooks/use-subscriptions";
import { AssignSubscriptionForm } from "./assign-subscription-form";
import { ChangePlanForm } from "./change-plan-form";
import { AddPackageForm } from "./add-package-form";
import { SubscriptionHistoryDialog } from "./subscription-history-dialog";

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
            Place a company on a plan. If they already have a live subscription the messages are
            added to their balance and the expiry is extended; otherwise a fresh period starts today.
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

function AddPackageDialog() {
  const { isOpen, selectedCompanyId, close } = useAddPackageDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useAddPackage();

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
            Top up this company&apos;s message credits with an add-on package.
          </DialogDescription>
        </DialogHeader>
        <AddPackageForm
          companyId={isOpen ? selectedCompanyId : null}
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

export function SubscriptionDialogs() {
  return (
    <>
      <AssignSubscriptionDialog />
      <ChangePlanDialog />
      <CancelSubscriptionDialog />
      <AddPackageDialog />
      <SubscriptionHistoryDialog />
    </>
  );
}
