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
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { Progress } from "@/components/ui/progress";
import {
  useCreateCampaignDialog,
  useEditCampaignDialog,
  useDeleteCampaignDialog,
  useCancelCampaignDialog,
  useRecipientsDialog,
} from "@/features/campaigns/store/campaign-store";
import {
  useCampaign,
  useCreateCampaign,
  useUpdateCampaign,
  useDeleteCampaign,
  useCancelCampaign,
  useCampaignStats,
} from "@/features/campaigns/hooks/use-campaigns";
import { CampaignForm } from "./campaign-form";
import type {
  CreateCampaignInput,
  UpdateCampaignInput,
} from "@/features/campaigns/schema/campaign-schema";

function CreateCampaignDialog() {
  const { isOpen, close } = useCreateCampaignDialog();
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useCreateCampaign();

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
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Create Campaign</DialogTitle>
          <DialogDescription>
            Send a WhatsApp template message to one or more contact lists.
          </DialogDescription>
        </DialogHeader>
        <CampaignForm
          mode="create"
          onSubmit={(data) => mutate(data as CreateCampaignInput)}
          isLoading={isPending}
          fieldErrors={fieldErrors}
        />
      </DialogContent>
    </Dialog>
  );
}

function EditCampaignDialog() {
  const { isOpen, selectedId, close } = useEditCampaignDialog();
  const { data: campaign, isLoading } = useCampaign(isOpen ? selectedId : null);
  const { mutate, isPending, fieldErrors, clearFieldErrors } = useUpdateCampaign();

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
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Edit Campaign</DialogTitle>
          <DialogDescription>Only Draft campaigns can be edited.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : (
          <CampaignForm
            mode="edit"
            defaultValues={campaign}
            onSubmit={(data) => {
              if (!selectedId) return;
              mutate({ id: selectedId, data: data as UpdateCampaignInput });
            }}
            isLoading={isPending}
            fieldErrors={fieldErrors}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function DeleteCampaignDialog() {
  const { isOpen, selectedId, close } = useDeleteCampaignDialog();
  const { mutate, isPending } = useDeleteCampaign();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete campaign?</AlertDialogTitle>
          <AlertDialogDescription>
            This permanently removes the Draft campaign. This action cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
          >
            {isPending && <Spinner className="mr-2" />}
            Delete
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function CancelCampaignDialog() {
  const { isOpen, selectedId, close } = useCancelCampaignDialog();
  const { mutate, isPending } = useCancelCampaign();

  return (
    <AlertDialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Cancel campaign?</AlertDialogTitle>
          <AlertDialogDescription>
            All queued recipients will be marked as Skipped. This cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Keep running</AlertDialogCancel>
          <AlertDialogAction
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            disabled={isPending}
            onClick={(e) => {
              e.preventDefault();
              if (selectedId) mutate(selectedId);
            }}
          >
            {isPending && <Spinner className="mr-2" />}
            Cancel campaign
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

const STATUS_BADGE: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Queued: "secondary",
  Sent: "outline",
  Delivered: "default",
  Read: "default",
  Failed: "destructive",
  Skipped: "secondary",
};

function RecipientsDialog() {
  const { isOpen, selectedId, close } = useRecipientsDialog();
  const { data: stats, isLoading } = useCampaignStats(isOpen ? selectedId : null);

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Campaign stats</DialogTitle>
          <DialogDescription>Real-time delivery breakdown.</DialogDescription>
        </DialogHeader>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="size-6" />
          </div>
        ) : stats ? (
          <div className="space-y-4">
            {(() => {
              const total = stats.totalRecipients;
              const processed = total - stats.queued;
              const percent = total > 0 ? Math.round((processed / total) * 100) : 0;
              const inProgress = stats.queued > 0;
              return (
                <div className="space-y-2">
                  <div className="flex items-center justify-between text-sm">
                    <span className="flex items-center gap-2 font-medium">
                      {inProgress ? (
                        <>
                          <Spinner className="size-3.5" />
                          Sending…
                        </>
                      ) : (
                        "Completed"
                      )}
                    </span>
                    <span className="text-muted-foreground">
                      {processed.toLocaleString()} of {total.toLocaleString()} ({percent}%)
                    </span>
                  </div>
                  <Progress value={percent} />
                </div>
              );
            })()}
            <div className="grid grid-cols-2 gap-3">
              {(
                [
                  ["Total", stats.totalRecipients],
                  ["Queued", stats.queued],
                  ["Sent", stats.sent],
                  ["Delivered", stats.delivered],
                  ["Read", stats.read],
                  ["Failed", stats.failed],
                  ["Skipped", stats.skipped],
                ] as [string, number][]
              ).map(([label, count]) => (
                <div key={label} className="rounded-lg border p-3">
                  <div className="text-xs text-muted-foreground">{label}</div>
                  <div className="mt-1 text-2xl font-bold">{count.toLocaleString()}</div>
                </div>
              ))}
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">Delivery rate</div>
                <div className="mt-1 text-2xl font-bold">{stats.deliveryRate}%</div>
              </div>
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">Read rate</div>
                <div className="mt-1 text-2xl font-bold">{stats.readRate}%</div>
              </div>
            </div>
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

export function CampaignDialogs() {
  return (
    <>
      <CreateCampaignDialog />
      <EditCampaignDialog />
      <DeleteCampaignDialog />
      <CancelCampaignDialog />
      <RecipientsDialog />
    </>
  );
}
