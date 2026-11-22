"use client";

import { useMemo, useState, useSyncExternalStore } from "react";
import Link from "next/link";
import { ArrowLeft, Globe } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Spinner } from "@/components/ui/spinner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useCalendarSettings, useUpdateTimeZone } from "@/features/calendar/hooks/use-calendar";
import { BusinessHoursEditor } from "./business-hours-editor";
import { AppointmentTypeSection } from "./appointment-type-section";
import { BlackoutSection } from "./blackout-section";

/**
 * The zones this picker offers.
 *
 * Scoped to the UAE, which is a single IANA zone: `Asia/Dubai` (UTC+4, no daylight saving). The
 * browser can enumerate all ~400 zones, but offering them makes the right answer harder to find and
 * lets an operator pick a plausible-looking neighbour by mistake — and the zone silently governs every
 * opening hour and appointment time on the calendar.
 *
 * To serve another country later, add its zone here; nothing else in the module is region-specific.
 */
const OFFERED_ZONES = ["Asia/Dubai"];

/** "Asia/Dubai (GMT+4)" — the offset makes the choice checkable at a glance. */
function zoneLabel(id: string): string {
  try {
    const offset = new Intl.DateTimeFormat("en-US", { timeZone: id, timeZoneName: "shortOffset" })
      .formatToParts(new Date())
      .find((p) => p.type === "timeZoneName")?.value;
    return offset ? `${id} (${offset})` : id;
  } catch {
    // An id this browser cannot resolve still has to render — it is the stored value.
    return id;
  }
}

function useTimeZoneOptions(current: string): string[] {
  return useMemo(
    // Always include whatever is stored, even when it is outside the offered set — otherwise the
    // Select renders blank and an innocent save silently changes the company's zone.
    () => (OFFERED_ZONES.includes(current) ? OFFERED_ZONES : [current, ...OFFERED_ZONES]),
    [current]
  );
}

/**
 * A value that changes every `ms`, used to re-render the clock.
 *
 * `useSyncExternalStore` rather than an interval in an effect: the server snapshot is null, so the
 * clock renders nothing until hydration instead of shipping a server-rendered time that is already
 * wrong by the time it reaches the browser.
 */
function useClockTick(ms = 30_000): number | null {
  return useSyncExternalStore(
    (onChange) => {
      const id = setInterval(onChange, ms);
      return () => clearInterval(id);
    },
    () => Math.floor(Date.now() / ms),
    () => null
  );
}

function TimeZoneCard({ timeZoneId }: { timeZoneId: string }) {
  const update = useUpdateTimeZone();
  const zones = useTimeZoneOptions(timeZoneId);
  // Seeded once per mount. The parent keys this card on the saved zone, so a successful save
  // remounts it with the new value rather than needing an effect to sync the two.
  const [value, setValue] = useState(timeZoneId);
  const tick = useClockTick();

  // Reads against the SELECTED zone, not the saved one, so it previews the choice before saving —
  // which is the fastest way to tell whether the zone is the right one.
  const timeThere =
    tick === null
      ? null
      : (() => {
          try {
            return new Intl.DateTimeFormat("en-GB", {
              timeZone: value,
              hour: "2-digit",
              minute: "2-digit",
              hour12: false,
            }).format(new Date());
          } catch {
            return null;
          }
        })();

  const unsaved = value !== timeZoneId;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Timezone</CardTitle>
        <CardDescription>
          The zone this business runs in. Opening hours are read against it, and every appointment
          time on the calendar is shown in it.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {/* Control and its Save sit together. Previously they were at opposite ends of the card,
            with the button a full card-width from the thing it saved. */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <Select value={value} onValueChange={setValue}>
            <SelectTrigger className="w-full sm:w-[260px]">
              <SelectValue placeholder="Choose a timezone" />
            </SelectTrigger>
            <SelectContent className="max-h-72">
              {zones.map((z) => (
                <SelectItem key={z} value={z}>
                  {zoneLabel(z)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Button
            type="button"
            onClick={() => update.mutate(value)}
            disabled={update.isPending || !unsaved}
            className="sm:shrink-0"
          >
            {update.isPending ? <Spinner className="size-4" /> : <Globe className="size-4" />}
            Save timezone
          </Button>

          {/* The confirmation that the zone is the right one, which a name alone cannot give. */}
          {timeThere && (
            <p className="text-sm text-muted-foreground sm:ml-1">
              It&rsquo;s {timeThere} there right now
              {unsaved && <span className="text-foreground"> — not saved yet</span>}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

export function CalendarSettingsView() {
  const { data: settings, isLoading } = useCalendarSettings();

  if (isLoading || !settings) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-64 w-full" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <Button variant="ghost" size="sm" asChild className="-ml-2">
        <Link href="/calendar">
          <ArrowLeft className="size-4" /> Back to calendar
        </Link>
      </Button>

      {/* Keyed so a saved change remounts the card with the new value — no prop/state sync effect. */}
      <TimeZoneCard key={settings.timeZoneId} timeZoneId={settings.timeZoneId} />
      <AppointmentTypeSection />
      <BusinessHoursEditor
        key={settings.businessHours.map((w) => `${w.id}:${w.startLocal}-${w.endLocal}`).join("|")}
        windows={settings.businessHours}
      />
      <BlackoutSection timeZoneId={settings.timeZoneId} />
    </div>
  );
}
