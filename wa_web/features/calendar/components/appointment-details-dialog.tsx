"use client";

import { useState } from "react";
import { Ban, Check, CircleSlash, Clock } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { Spinner } from "@/components/ui/spinner";
import {
  useCancelAppointment,
  useRescheduleAppointment,
  useUpdateAppointmentStatus,
} from "@/features/calendar/hooks/use-calendar";
import { useAvailability } from "@/features/calendar/hooks/use-calendar";
import type { Appointment } from "@/features/calendar/types";
import { dateInZone, dateTimeInZone, timeInZone } from "@/features/calendar/components/zone";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";

const STATUS_VARIANT: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Pending: "outline",
  Confirmed: "default",
  Completed: "secondary",
  NoShow: "destructive",
  Cancelled: "destructive",
};

export function AppointmentDetailsDialog({
  appointment,
  timeZoneId,
  onClose,
}: {
  appointment: Appointment | null;
  timeZoneId: string;
  onClose: () => void;
}) {
  if (!appointment) return null;

  // Keyed on the appointment so selecting a different one remounts with fresh state — no effect
  // syncing props into state, and no chance of the previous booking's draft leaking across.
  return (
    <DetailsDialog
      key={appointment.id}
      appointment={appointment}
      timeZoneId={timeZoneId}
      onClose={onClose}
    />
  );
}

function DetailsDialog({
  appointment,
  timeZoneId,
  onClose,
}: {
  appointment: Appointment;
  timeZoneId: string;
  onClose: () => void;
}) {
  const cancel = useCancelAppointment();
  const reschedule = useRescheduleAppointment();
  const status = useUpdateAppointmentStatus();

  const [mode, setMode] = useState<"view" | "cancel" | "reschedule">("view");
  const [reason, setReason] = useState("");
  const [newDate, setNewDate] = useState(() => dateInZone(appointment.startUtc, timeZoneId));
  const [newSlotUtc, setNewSlotUtc] = useState("");

  // Only fetched once the operator actually opens the reschedule pane.
  const { data: availability, isFetching: slotsLoading } = useAvailability(
    mode === "reschedule" ? appointment.appointmentTypeId : null,
    newDate,
    newDate
  );

  const isOpenEnded =
    appointment.status !== "Cancelled" && appointment.status !== "Completed";
  const busy = cancel.isPending || reschedule.isPending || status.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {appointment.contactName}
            <Badge variant={STATUS_VARIANT[appointment.status] ?? "default"}>
              {appointment.status}
            </Badge>
          </DialogTitle>
          <DialogDescription>{appointment.appointmentTypeName}</DialogDescription>
        </DialogHeader>

        <dl className="space-y-2 text-sm">
          <div className="flex justify-between gap-4">
            <dt className="text-muted-foreground">When</dt>
            <dd className="text-right">
              {dateTimeInZone(appointment.startUtc, timeZoneId)} –{" "}
              {timeInZone(appointment.endUtc, timeZoneId)}
            </dd>
          </div>
          <div className="flex justify-between gap-4">
            <dt className="text-muted-foreground">Phone</dt>
            <dd>{appointment.contactPhone}</dd>
          </div>
          <div className="flex justify-between gap-4">
            <dt className="text-muted-foreground">Booked via</dt>
            <dd>{appointment.source}</dd>
          </div>
          {appointment.externalSyncStatus !== "NotApplicable" && (
            <div className="flex justify-between gap-4">
              <dt className="text-muted-foreground">Clinic CRM</dt>
              <dd className="text-right">{clinicCrmLabel(appointment)}</dd>
            </div>
          )}
          {appointment.externalSyncError && (
            <div className="space-y-1">
              <dt className="text-muted-foreground">Clinic CRM note</dt>
              <dd className="rounded-md bg-muted/60 px-3 py-2">{appointment.externalSyncError}</dd>
            </div>
          )}
          {appointment.notes && (
            <div className="space-y-1">
              <dt className="text-muted-foreground">Notes</dt>
              <dd className="rounded-md bg-muted/60 px-3 py-2">{appointment.notes}</dd>
            </div>
          )}
          {appointment.cancellationReason && (
            <div className="space-y-1">
              <dt className="text-muted-foreground">Cancellation reason</dt>
              <dd className="rounded-md bg-muted/60 px-3 py-2">{appointment.cancellationReason}</dd>
            </div>
          )}
        </dl>

        {mode === "cancel" && (
          <>
            <Separator />
            <div className="space-y-2">
              <Label htmlFor="cancel-reason">Why is this being cancelled? (optional)</Label>
              <Input
                id="cancel-reason"
                value={reason}
                maxLength={500}
                onChange={(e) => setReason(e.target.value)}
                placeholder="Customer rescheduled by phone"
              />
              <p className="text-xs text-muted-foreground">
                Cancelling frees the slot immediately. The customer is not messaged automatically.
                {appointment.externalSyncStatus === "Synced" &&
                  " The clinic's CRM cannot be cancelled from here: the booking there is flagged" +
                    " \"CANCELLATION REQUESTED\" and someone must cancel it in the CRM."}
              </p>
            </div>
          </>
        )}

        {mode === "reschedule" && (
          <>
            <Separator />
            <div className="space-y-3">
              <div className="space-y-2">
                <Label htmlFor="new-date">New day</Label>
                <Input
                  id="new-date"
                  type="date"
                  value={newDate}
                  onChange={(e) => {
                    setNewDate(e.target.value);
                    setNewSlotUtc("");
                  }}
                />
              </div>
              <div className="space-y-2">
                <Label>New time</Label>
                {slotsLoading ? (
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Spinner className="size-4" /> Checking availability…
                  </div>
                ) : availability && availability.slots.length > 0 ? (
                  <ToggleGroup
                    type="single"
                    variant="outline"
                    value={newSlotUtc ?? ""}
                    onValueChange={(v) => v && setNewSlotUtc(v)}
                    className="grid max-h-40 w-full grid-cols-3 gap-2 overflow-y-auto rounded-md border p-2 sm:grid-cols-4"
                  >
                    {availability.slots.map((s) => (
                      <ToggleGroupItem
                        key={s.startUtc}
                        value={s.startUtc}
                        className="h-8 w-full px-2 font-normal data-[state=on]:border-primary data-[state=on]:bg-primary data-[state=on]:text-primary-foreground"
                      >
                        {timeInZone(s.startUtc, timeZoneId)}
                      </ToggleGroupItem>
                    ))}
                  </ToggleGroup>
                ) : (
                  <p className="rounded-md border border-dashed px-3 py-2 text-sm text-muted-foreground">
                    Nothing free on this day.
                  </p>
                )}
              </div>
            </div>
          </>
        )}

        <DialogFooter className="flex-wrap gap-2">
          {mode === "view" && (
            <>
              {isOpenEnded && (
                <>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={busy}
                    onClick={() => setMode("reschedule")}
                  >
                    <Clock className="size-4" /> Reschedule
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={busy}
                    onClick={() => status.mutate({ id: appointment.id, status: "Completed" })}
                  >
                    <Check className="size-4" /> Completed
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={busy}
                    onClick={() => status.mutate({ id: appointment.id, status: "NoShow" })}
                  >
                    <CircleSlash className="size-4" /> No-show
                  </Button>
                  <Button
                    type="button"
                    variant="destructive"
                    size="sm"
                    disabled={busy}
                    onClick={() => setMode("cancel")}
                  >
                    <Ban className="size-4" /> Cancel
                  </Button>
                </>
              )}
              <Button type="button" variant="ghost" size="sm" onClick={onClose}>
                Close
              </Button>
            </>
          )}

          {mode === "cancel" && (
            <>
              <Button type="button" variant="outline" onClick={() => setMode("view")} disabled={busy}>
                Back
              </Button>
              <Button
                type="button"
                variant="destructive"
                disabled={busy}
                onClick={() =>
                  cancel.mutate(
                    { id: appointment.id, data: { reason: reason.trim() || undefined } },
                    { onSuccess: onClose }
                  )
                }
              >
                {cancel.isPending ? <Spinner className="size-4" /> : <Ban className="size-4" />}
                Cancel appointment
              </Button>
            </>
          )}

          {mode === "reschedule" && (
            <>
              <Button type="button" variant="outline" onClick={() => setMode("view")} disabled={busy}>
                Back
              </Button>
              <Button
                type="button"
                disabled={busy || !newSlotUtc}
                onClick={() =>
                  reschedule.mutate(
                    { id: appointment.id, startUtc: newSlotUtc },
                    { onSuccess: onClose }
                  )
                }
              >
                {reschedule.isPending ? <Spinner className="size-4" /> : <Clock className="size-4" />}
                Move appointment
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** How the booking stands with the clinic's CRM, in words staff can act on. */
function clinicCrmLabel(appointment: Appointment) {
  const number = appointment.externalBookingId ? `#${appointment.externalBookingId}` : "";
  switch (appointment.externalSyncStatus) {
    case "Synced":
      return `Booked ${number}`.trim();
    case "Pending":
      return "Waiting to reach the clinic";
    case "Failed":
      return "Did not reach the clinic";
    case "CancelRequested":
      return `Cancellation flagged on ${number} — cancel it in the CRM`;
    default:
      return number;
  }
}
