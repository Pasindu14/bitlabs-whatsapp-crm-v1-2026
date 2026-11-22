"use client";

import { useState } from "react";
import { CalendarOff, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Spinner } from "@/components/ui/spinner";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
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
import { cn } from "@/lib/utils";
import {
  useBlackouts,
  useCreateBlackout,
  useDeleteBlackout,
} from "@/features/calendar/hooks/use-calendar";
import { useBlackoutDialog } from "@/features/calendar/store/calendar-store";
import type { Blackout } from "@/features/calendar/types";
import { addDays, timeInZone, todayInZone, zonedToUtc } from "./zone";

// ── Reading a span back as a sentence ────────────────────────────────────────

const fmt = (iso: string, tz: string, opts: Intl.DateTimeFormatOptions) =>
  new Intl.DateTimeFormat("en-GB", { timeZone: tz, ...opts }).format(new Date(iso));

const withWeekday = (iso: string, tz: string) =>
  fmt(iso, tz, { weekday: "long", day: "numeric", month: "long" });

const dayOnly = (iso: string, tz: string) => fmt(iso, tz, { day: "numeric", month: "long" });

/** How long the span runs, in the largest unit that reads naturally. */
function describeLength(startIso: string, endIso: string): string {
  const minutes = Math.round((new Date(endIso).getTime() - new Date(startIso).getTime()) / 60000);
  if (minutes % 1440 === 0) {
    const days = minutes / 1440;
    return `${days} ${days === 1 ? "day" : "days"}`;
  }
  if (minutes >= 60 && minutes % 60 === 0) {
    const hours = minutes / 60;
    return `${hours} ${hours === 1 ? "hour" : "hours"}`;
  }
  return `${minutes} min`;
}

/**
 * Turns two stored instants into something readable.
 *
 * A closed period is nearly always whole days, and rendering that as
 * "Mon 1 Jun, 00:00 – Tue 2 Jun, 00:00" makes the commonest case the hardest to read. Ends are
 * exclusive throughout this module, so a whole day ends at the next midnight — which is what lets a
 * span be recognised as all-day and written the way a person would say it.
 */
function describeSpan(b: Blackout, tz: string): string {
  const startsAtMidnight = timeInZone(b.startUtc, tz) === "00:00";
  const endsAtMidnight = timeInZone(b.endUtc, tz) === "00:00";

  if (startsAtMidnight && endsAtMidnight) {
    // One millisecond before the exclusive end lands on the last day actually covered.
    const lastDay = new Date(new Date(b.endUtc).getTime() - 1).toISOString();
    const singleDay = fmt(b.startUtc, tz, { day: "numeric", month: "long", year: "numeric" }) ===
      fmt(lastDay, tz, { day: "numeric", month: "long", year: "numeric" });

    return singleDay
      ? `${withWeekday(b.startUtc, tz)}, all day`
      : `${dayOnly(b.startUtc, tz)} to ${dayOnly(lastDay, tz)}`;
  }

  const sameDay =
    fmt(b.startUtc, tz, { day: "numeric", month: "long", year: "numeric" }) ===
    fmt(b.endUtc, tz, { day: "numeric", month: "long", year: "numeric" });

  return sameDay
    ? `${withWeekday(b.startUtc, tz)}, ${timeInZone(b.startUtc, tz)} to ${timeInZone(b.endUtc, tz)}`
    : `${withWeekday(b.startUtc, tz)} ${timeInZone(b.startUtc, tz)} to ${withWeekday(b.endUtc, tz)} ${timeInZone(b.endUtc, tz)}`;
}

type Phase = "past" | "now" | "upcoming";

function phaseOf(b: Blackout): Phase {
  const now = Date.now();
  if (new Date(b.endUtc).getTime() <= now) return "past";
  if (new Date(b.startUtc).getTime() <= now) return "now";
  return "upcoming";
}

/**
 * Days and part-days the calendar is closed regardless of opening hours — holidays, leave, a closed
 * afternoon. Entered in the company's local time and stored as UTC instants.
 */
export function BlackoutSection({ timeZoneId }: { timeZoneId: string }) {
  const today = todayInZone(timeZoneId);

  // A rolling year is enough to manage: anything further out is easier to add closer to the time.
  const fromUtc = zonedToUtc(addDays(today, -30), "00:00", timeZoneId).toISOString();
  const toUtc = zonedToUtc(addDays(today, 365), "00:00", timeZoneId).toISOString();

  const { data: blackouts, isLoading } = useBlackouts(fromUtc, toUtc);
  const remove = useDeleteBlackout();
  const dialog = useBlackoutDialog();
  const [confirming, setConfirming] = useState<Blackout | null>(null);

  // What is still to come governs bookings, so it leads; what has ended is history and follows,
  // dimmed. Ordering and weight carry that rather than a pair of section headings.
  const ordered = [...(blackouts ?? [])].sort((a, b) => {
    const pa = phaseOf(a) === "past" ? 1 : 0;
    const pb = phaseOf(b) === "past" ? 1 : 0;
    if (pa !== pb) return pa - pb;
    const dir = pa === 1 ? -1 : 1; // upcoming soonest first; past most-recent first
    return dir * (new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime());
  });

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
        <div>
          <CardTitle>Closed periods</CardTitle>
          <CardDescription>
            Holidays and time off. These override opening hours, so nothing can be booked inside them.
          </CardDescription>
        </div>
        <Button type="button" size="sm" onClick={dialog.open}>
          <Plus className="size-4" /> Block time
        </Button>
      </CardHeader>

      <CardContent>
        {isLoading ? (
          <Skeleton className="h-20 w-full" />
        ) : ordered.length > 0 ? (
          <ul className="divide-y overflow-hidden rounded-lg border">
            {ordered.map((b) => {
              const phase = phaseOf(b);
              return (
                <li
                  key={b.id}
                  className={cn(
                    "flex items-start justify-between gap-4 px-4 py-3",
                    phase === "past" && "bg-muted/30"
                  )}
                >
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span
                        className={cn(
                          "truncate font-medium",
                          phase === "past" && "text-muted-foreground"
                        )}
                      >
                        {b.reason}
                      </span>
                      {/* Only states that change what happens next are marked. */}
                      {phase === "now" && <Badge>Closed now</Badge>}
                      {phase === "past" && <Badge variant="outline">Ended</Badge>}
                    </div>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {describeSpan(b, timeZoneId)}
                    </p>
                    <p className="mt-0.5 text-xs text-muted-foreground">
                      {describeLength(b.startUtc, b.endUtc)}
                    </p>
                  </div>

                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-8 shrink-0 text-muted-foreground hover:text-destructive"
                    aria-label={`Remove ${b.reason}`}
                    onClick={() => setConfirming(b)}
                  >
                    <Trash2 className="size-4" />
                  </Button>
                </li>
              );
            })}
          </ul>
        ) : (
          <div className="rounded-lg border border-dashed px-4 py-8 text-center">
            <p className="text-sm text-muted-foreground">
              Nothing blocked. Your opening hours apply every week.
            </p>
            <Button type="button" variant="outline" size="sm" className="mt-3" onClick={dialog.open}>
              <Plus className="size-4" /> Block time
            </Button>
          </div>
        )}
      </CardContent>

      <BlackoutDialog timeZoneId={timeZoneId} />

      <AlertDialog open={!!confirming} onOpenChange={(o) => !o && setConfirming(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove {confirming?.reason}?</AlertDialogTitle>
            <AlertDialogDescription>
              {confirming && phaseOf(confirming) === "past"
                ? "This period has already ended, so removing it changes nothing on the calendar."
                : "Those times become bookable again, following your normal opening hours."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={remove.isPending}>Keep it</AlertDialogCancel>
            <AlertDialogAction
              disabled={remove.isPending}
              onClick={() => {
                if (confirming) remove.mutate(confirming.id, { onSuccess: () => setConfirming(null) });
              }}
            >
              {remove.isPending && <Spinner className="size-4" />}
              Remove
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}

function BlackoutDialog({ timeZoneId }: { timeZoneId: string }) {
  const { isOpen, close } = useBlackoutDialog();

  // Mounted only while open, so every open starts clean.
  if (!isOpen) return null;
  return <BlackoutForm timeZoneId={timeZoneId} onClose={close} />;
}

function BlackoutForm({ timeZoneId, onClose }: { timeZoneId: string; onClose: () => void }) {
  const { mutate, isPending } = useCreateBlackout();
  const today = todayInZone(timeZoneId);

  const [allDay, setAllDay] = useState(true);
  const [startDate, setStartDate] = useState(today);
  const [startTime, setStartTime] = useState("09:00");
  const [endDate, setEndDate] = useState(today);
  const [endTime, setEndTime] = useState("17:00");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    if (!reason.trim()) {
      setError("Give it a name so it is recognisable on the calendar.");
      return;
    }

    // Converted from the COMPANY's wall clock, not the browser's — an operator abroad blocking
    // a public holiday means the business's day, not their own.
    //
    // All-day runs from midnight to the midnight AFTER the last day chosen. Ends are exclusive
    // everywhere in this module, so closing at 23:59 instead would leave that final minute bookable.
    const start = allDay
      ? zonedToUtc(startDate, "00:00", timeZoneId)
      : zonedToUtc(startDate, startTime, timeZoneId);
    const end = allDay
      ? zonedToUtc(addDays(endDate, 1), "00:00", timeZoneId)
      : zonedToUtc(endDate, endTime, timeZoneId);

    if (end <= start) {
      setError(
        allDay ? "The last day cannot be before the first." : "The end must be after the start."
      );
      return;
    }
    setError(null);

    mutate({ startUtc: start.toISOString(), endUtc: end.toISOString(), reason: reason.trim() });
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Block time</DialogTitle>
          <DialogDescription>Entered in {timeZoneId}.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="blackout-reason">What is it</Label>
            <Input
              id="blackout-reason"
              value={reason}
              maxLength={200}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Eid al-Fitr"
              autoFocus
            />
          </div>

          {/* Whole days are the common case, so they are the default and need only two fields. */}
          <div className="flex items-center justify-between rounded-lg border px-3 py-2.5">
            <Label htmlFor="all-day" className="cursor-pointer font-normal">
              Whole days
            </Label>
            <Switch id="all-day" checked={allDay} onCheckedChange={setAllDay} />
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="blackout-start-date">{allDay ? "First day" : "From"}</Label>
              <Input
                id="blackout-start-date"
                type="date"
                value={startDate}
                onChange={(e) => {
                  setStartDate(e.target.value);
                  if (endDate < e.target.value) setEndDate(e.target.value);
                }}
              />
            </div>
            {!allDay && (
              <div className="space-y-2">
                <Label htmlFor="blackout-start-time">Time</Label>
                <Input
                  id="blackout-start-time"
                  type="time"
                  value={startTime}
                  onChange={(e) => setStartTime(e.target.value)}
                />
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="blackout-end-date">{allDay ? "Last day" : "To"}</Label>
              <Input
                id="blackout-end-date"
                type="date"
                value={endDate}
                min={startDate}
                onChange={(e) => setEndDate(e.target.value)}
              />
            </div>
            {!allDay && (
              <div className="space-y-2">
                <Label htmlFor="blackout-end-time">Time</Label>
                <Input
                  id="blackout-end-time"
                  type="time"
                  value={endTime}
                  onChange={(e) => setEndTime(e.target.value)}
                />
              </div>
            )}
          </div>

          {allDay && (
            <p className="text-xs text-muted-foreground">
              Both days are included. Pick the same day twice to close for one day.
            </p>
          )}

          {error && <p className="text-sm text-destructive">{error}</p>}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isPending}>
            Cancel
          </Button>
          <Button type="button" onClick={submit} disabled={isPending}>
            {isPending ? <Spinner className="size-4" /> : <CalendarOff className="size-4" />}
            Block time
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
