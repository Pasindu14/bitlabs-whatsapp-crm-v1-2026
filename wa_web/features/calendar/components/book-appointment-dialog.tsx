"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { CalendarClock, Search } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getContactsAction } from "@/features/contacts/actions/contact-actions";
import { useBookDialog } from "@/features/calendar/store/calendar-store";
import {
  useAppointmentTypes,
  useAvailability,
  useCreateAppointment,
} from "@/features/calendar/hooks/use-calendar";
import { dateInZone, timeInZone, todayInZone } from "@/features/calendar/components/zone";

/**
 * Books a slot on the shared calendar.
 *
 * The operator never types a time. They pick a service, a day, and then one of the slots the API says
 * is genuinely free — so the booking that gets submitted is one the availability engine already
 * approved. A slot can still be taken between rendering and submitting, which surfaces as SLOT_TAKEN
 * and refreshes the list.
 */
export function BookAppointmentDialog({ timeZoneId }: { timeZoneId: string }) {
  const { isOpen, prefillStartUtc, close } = useBookDialog();

  // Mounted only while open, so every open starts from clean state — no effect resetting a previous
  // booking's service, day and slot out of the form.
  if (!isOpen) return null;

  return (
    <BookDialog timeZoneId={timeZoneId} prefillStartUtc={prefillStartUtc} onClose={close} />
  );
}

function BookDialog({
  timeZoneId,
  prefillStartUtc,
  onClose,
}: {
  timeZoneId: string;
  prefillStartUtc: string | null;
  onClose: () => void;
}) {
  const { mutate, isPending } = useCreateAppointment();
  const { data: types, isLoading: typesLoading } = useAppointmentTypes(true);

  const [pickedTypeId, setPickedTypeId] = useState<string>("");
  const [date, setDate] = useState<string>(() =>
    prefillStartUtc ? dateInZone(prefillStartUtc, timeZoneId) : todayInZone(timeZoneId)
  );
  const [slotUtc, setSlotUtc] = useState<string>(prefillStartUtc ?? "");
  const [contactId, setContactId] = useState<string>("");
  const [contactSearch, setContactSearch] = useState("");
  const [notes, setNotes] = useState("");

  // Services arrive after mount, so the default falls back to the first one until the operator picks.
  // Deriving it beats seeding state from an effect once the fetch resolves.
  const typeId = pickedTypeId || types?.[0]?.id || "";

  // Availability is fetched for the single chosen day, which keeps the payload small and the list short.
  const { data: availability, isFetching: slotsLoading } = useAvailability(
    typeId || null,
    date,
    date
  );

  const { data: contacts, isFetching: contactsLoading } = useQuery({
    queryKey: queryKeys.contacts.list({ page: 1, pageSize: 20, search: contactSearch }),
    queryFn: async () => {
      const res = await getContactsAction({ page: 1, pageSize: 20, search: contactSearch || undefined });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
  });

  const selectedContact = useMemo(
    () => contacts?.find((c) => c.id === contactId),
    [contacts, contactId]
  );

  const canSubmit = !!typeId && !!slotUtc && !!contactId && !isPending;

  const submit = () => {
    if (!canSubmit) return;
    mutate({
      contactId,
      appointmentTypeId: typeId,
      startUtc: slotUtc,
      notes: notes.trim() || undefined,
    });
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Book an appointment</DialogTitle>
          <DialogDescription>
            Times are shown in {timeZoneId}, the timezone this calendar runs on.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {/* Service */}
          <div className="space-y-2">
            <Label htmlFor="service">Service</Label>
            {typesLoading ? (
              <Spinner className="size-4" />
            ) : types && types.length > 0 ? (
              <Select
                value={typeId}
                onValueChange={(v) => {
                  setPickedTypeId(v);
                  setSlotUtc("");   // slot lengths differ per service — a carried-over pick would be wrong
                }}
              >
                <SelectTrigger id="service">
                  <SelectValue placeholder="Choose a service" />
                </SelectTrigger>
                <SelectContent>
                  {types.map((t) => (
                    <SelectItem key={t.id} value={t.id}>
                      {t.name} · {t.durationMinutes} min
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : (
              <p className="rounded-md border border-dashed px-3 py-2 text-sm text-muted-foreground">
                No bookable services yet. Add one in calendar settings first.
              </p>
            )}
          </div>

          {/* Day */}
          <div className="space-y-2">
            <Label htmlFor="date">Day</Label>
            <Input
              id="date"
              type="date"
              value={date}
              min={todayInZone(timeZoneId)}
              onChange={(e) => {
                setDate(e.target.value);
                setSlotUtc("");
              }}
            />
          </div>

          {/* Slots */}
          <div className="space-y-2">
            <Label>Available times</Label>
            {!typeId ? (
              <p className="text-sm text-muted-foreground">Choose a service to see available times.</p>
            ) : slotsLoading ? (
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <Spinner className="size-4" /> Checking availability…
              </div>
            ) : availability && availability.slots.length > 0 ? (
              <div className="grid max-h-48 grid-cols-3 gap-2 overflow-y-auto rounded-md border p-2 sm:grid-cols-4">
                {availability.slots.map((s) => (
                  <button
                    key={s.startUtc}
                    type="button"
                    onClick={() => setSlotUtc(s.startUtc)}
                    className={cn(
                      "rounded-md border px-2 py-1.5 text-sm transition",
                      slotUtc === s.startUtc
                        ? "border-primary bg-primary text-primary-foreground"
                        : "hover:bg-muted"
                    )}
                  >
                    {timeInZone(s.startUtc, timeZoneId)}
                  </button>
                ))}
              </div>
            ) : (
              <p className="rounded-md border border-dashed px-3 py-2 text-sm text-muted-foreground">
                Nothing free on this day. Try another date, or check your opening hours and blackouts.
              </p>
            )}
          </div>

          {/* Contact */}
          <div className="space-y-2">
            <Label htmlFor="contact-search">Customer</Label>
            <div className="relative">
              <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
              <Input
                id="contact-search"
                placeholder="Search by name or number"
                className="pl-8"
                value={contactSearch}
                onChange={(e) => setContactSearch(e.target.value)}
              />
            </div>

            {selectedContact ? (
              <div className="flex items-center justify-between rounded-md border bg-muted/40 px-3 py-2 text-sm">
                <span>
                  <span className="font-medium">{selectedContact.name}</span>{" "}
                  <span className="text-muted-foreground">{selectedContact.phone}</span>
                </span>
                <Button type="button" variant="ghost" size="sm" onClick={() => setContactId("")}>
                  Change
                </Button>
              </div>
            ) : contactsLoading ? (
              <Spinner className="size-4" />
            ) : (
              <div className="max-h-36 space-y-1 overflow-y-auto rounded-md border p-1">
                {contacts && contacts.length > 0 ? (
                  contacts.map((c) => (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => setContactId(c.id)}
                      className="flex w-full items-center justify-between rounded px-2 py-1.5 text-left text-sm hover:bg-muted"
                    >
                      <span>{c.name}</span>
                      <span className="text-xs text-muted-foreground">{c.phone}</span>
                    </button>
                  ))
                ) : (
                  <p className="px-2 py-1.5 text-sm text-muted-foreground">No contacts found.</p>
                )}
              </div>
            )}
          </div>

          {/* Notes */}
          <div className="space-y-2">
            <Label htmlFor="notes">Notes (optional)</Label>
            <Textarea
              id="notes"
              rows={2}
              maxLength={1000}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Anything the person taking this appointment should know"
            />
          </div>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isPending}>
            Cancel
          </Button>
          <Button type="button" onClick={submit} disabled={!canSubmit}>
            {isPending ? <Spinner className="size-4" /> : <CalendarClock className="size-4" />}
            Book
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
