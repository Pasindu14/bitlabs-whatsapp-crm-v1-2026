"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { CalendarPlus, ChevronLeft, ChevronRight, Settings2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  useAppointments,
  useBlackouts,
  useCalendarSettings,
} from "@/features/calendar/hooks/use-calendar";
import { useBookDialog, useCalendarAnchor } from "@/features/calendar/store/calendar-store";
import { WeekGrid } from "./week-grid";
import { BookAppointmentDialog } from "./book-appointment-dialog";
import { AppointmentDetailsDialog } from "./appointment-details-dialog";
import { addDays, shortDateLabel, todayInZone, weekDates, zonedToUtc } from "./zone";

/**
 * The company's shared calendar for one week at a time.
 *
 * Everything is rendered in the COMPANY's timezone, not the viewer's — an operator abroad must see the
 * business's own Monday morning. The week's UTC bounds are derived from the company-local dates so the
 * query asks for exactly the span the grid draws.
 */
export function CalendarView() {
  const { data: settings, isLoading: settingsLoading } = useCalendarSettings();
  const { anchorDate, setAnchorDate } = useCalendarAnchor();
  const book = useBookDialog();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const timeZoneId = settings?.timeZoneId ?? "UTC";
  const anchor = anchorDate ?? todayInZone(timeZoneId);
  const dates = useMemo(() => weekDates(anchor), [anchor]);

  // Local midnight on the first day through local midnight after the last — converted in the company's
  // zone, so a week is a true seven days there rather than a UTC approximation of one.
  const { fromUtc, toUtc } = useMemo(() => {
    if (!settings) return { fromUtc: "", toUtc: "" };
    return {
      fromUtc: zonedToUtc(dates[0], "00:00", timeZoneId).toISOString(),
      toUtc: zonedToUtc(addDays(dates[6], 1), "00:00", timeZoneId).toISOString(),
    };
  }, [settings, dates, timeZoneId]);

  const { data: appointments, isLoading: apptsLoading } = useAppointments(fromUtc, toUtc);
  const { data: blackouts } = useBlackouts(fromUtc, toUtc);

  const selected = appointments?.find((a) => a.id === selectedId) ?? null;

  if (settingsLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-[480px] w-full" />
      </div>
    );
  }

  const hasHours = (settings?.businessHours.length ?? 0) > 0;

  return (
    <div className="space-y-4">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="icon"
            aria-label="Previous week"
            onClick={() => setAnchorDate(addDays(anchor, -7))}
          >
            <ChevronLeft className="size-4" />
          </Button>
          <Button variant="outline" onClick={() => setAnchorDate(todayInZone(timeZoneId))}>
            Today
          </Button>
          <Button
            variant="outline"
            size="icon"
            aria-label="Next week"
            onClick={() => setAnchorDate(addDays(anchor, 7))}
          >
            <ChevronRight className="size-4" />
          </Button>
          <span className="ml-2 text-sm font-medium">
            {shortDateLabel(dates[0])} – {shortDateLabel(dates[6])}
          </span>
          <Badge variant="secondary" className="font-normal">
            {timeZoneId}
          </Badge>
        </div>

        <div className="flex items-center gap-2">
          <Button variant="outline" asChild>
            <Link href="/calendar/settings">
              <Settings2 className="size-4" /> Settings
            </Link>
          </Button>
          <Button onClick={() => book.open()}>
            <CalendarPlus className="size-4" /> Book appointment
          </Button>
        </div>
      </div>

      {!hasHours && (
        <div className="rounded-lg border border-dashed bg-muted/40 px-4 py-3 text-sm text-muted-foreground">
          No opening hours set yet, so nothing is bookable. Add them in{" "}
          <Link href="/calendar/settings" className="font-medium underline underline-offset-2">
            calendar settings
          </Link>
          .
        </div>
      )}

      {apptsLoading ? (
        <Skeleton className="h-[480px] w-full" />
      ) : (
        <WeekGrid
          dates={dates}
          timeZoneId={timeZoneId}
          appointments={appointments ?? []}
          businessHours={settings?.businessHours ?? []}
          blackouts={blackouts ?? []}
          onSelectAppointment={setSelectedId}
        />
      )}

      <BookAppointmentDialog timeZoneId={timeZoneId} />
      <AppointmentDetailsDialog
        appointment={selected}
        timeZoneId={timeZoneId}
        onClose={() => setSelectedId(null)}
      />
    </div>
  );
}
