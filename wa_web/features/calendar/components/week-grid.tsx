"use client";

import { cn } from "@/lib/utils";
import type { Appointment, BusinessHoursWindow, Blackout } from "@/features/calendar/types";
import {
  dateInZone,
  dayNameInZone,
  minutesBetween,
  minutesFromMidnight,
  shortDateLabel,
  timeInZone,
  todayInZone,
} from "@/features/calendar/components/zone";

/** Pixels per hour. Sets the whole grid's vertical scale. */
const HOUR_PX = 56;

/** Fallback window when a company has not set any opening hours yet. */
const DEFAULT_RANGE = { startHour: 8, endHour: 18 };

const STATUS_STYLES: Record<string, string> = {
  Pending: "border-amber-500/60 bg-amber-500/15 text-amber-900 dark:text-amber-100",
  Confirmed: "border-primary/60 bg-primary/15",
  Completed: "border-emerald-600/60 bg-emerald-600/15 text-emerald-900 dark:text-emerald-100",
  NoShow: "border-destructive/60 bg-destructive/15 text-destructive",
  Cancelled: "border-muted-foreground/40 bg-muted line-through text-muted-foreground",
};

/** Parses "09:00:00" into minutes from midnight. */
function toMinutes(local: string): number {
  const [h, m] = local.split(":").map(Number);
  return h * 60 + (m || 0);
}

/**
 * The hour span the grid renders — wide enough to contain every opening window AND every appointment
 * shown, padded by an hour on each side. Appointments count because they can fall well outside opening
 * hours: a clinic CRM books on its own hours (plan 014), and staff can book after hours. An appointment
 * above or below the grid is simply invisible, which reads as "it was never booked".
 */
function gridRange(hours: BusinessHoursWindow[], appointments: Appointment[], timeZoneId: string) {
  const starts = [
    ...hours.map((h) => toMinutes(h.startLocal)),
    ...appointments.map((a) => minutesFromMidnight(a.startUtc, timeZoneId)),
  ];
  const ends = [
    ...hours.map((h) => toMinutes(h.endLocal)),
    // An appointment crossing midnight ends "earlier" than it starts on the clock; run it to the bottom.
    ...appointments.map((a) => {
      const start = minutesFromMidnight(a.startUtc, timeZoneId);
      const end = minutesFromMidnight(a.endUtc, timeZoneId);
      return end > start ? end : 24 * 60;
    }),
  ];

  if (starts.length === 0) return DEFAULT_RANGE;

  return {
    startHour: Math.max(0, Math.floor(Math.min(...starts) / 60) - 1),
    endHour: Math.min(24, Math.ceil(Math.max(...ends) / 60) + 1),
  };
}

export function WeekGrid({
  dates,
  timeZoneId,
  appointments,
  businessHours,
  blackouts,
  onSelectAppointment,
}: {
  /** The seven "YYYY-MM-DD" dates to render, Monday first. */
  dates: string[];
  timeZoneId: string;
  appointments: Appointment[];
  businessHours: BusinessHoursWindow[];
  blackouts: Blackout[];
  onSelectAppointment: (id: string) => void;
}) {
  const { startHour, endHour } = gridRange(businessHours, appointments, timeZoneId);
  const gridStartMin = startHour * 60;
  const totalMinutes = (endHour - startHour) * 60;
  const bodyHeight = (totalMinutes / 60) * HOUR_PX;

  const today = todayInZone(timeZoneId);
  const hourLabels = Array.from({ length: endHour - startHour }, (_, i) => startHour + i);

  // Appointments are bucketed by the date they START on in the COMPANY's zone, which is the column
  // the operator expects them in — not the date they fall on in the viewer's own timezone.
  const byDate = new Map<string, Appointment[]>();
  for (const a of appointments) {
    const key = dateInZone(a.startUtc, timeZoneId);
    const list = byDate.get(key);
    if (list) list.push(a);
    else byDate.set(key, [a]);
  }

  const offsetPx = (instant: string) =>
    ((minutesFromMidnight(instant, timeZoneId) - gridStartMin) / 60) * HOUR_PX;

  return (
    <div className="overflow-x-auto rounded-xl border bg-card">
      <div className="min-w-[820px]">
        {/* Column headers */}
        <div className="flex border-b bg-muted/40">
          <div className="w-16 shrink-0 border-r px-2 py-2 text-[11px] font-medium text-muted-foreground">
            {timeZoneId.split("/").pop()?.replace(/_/g, " ")}
          </div>
          {dates.map((date) => {
            const isToday = date === today;
            return (
              <div
                key={date}
                className={cn(
                  "flex-1 border-r px-2 py-2 text-center last:border-r-0",
                  isToday && "bg-primary/10"
                )}
              >
                <div className={cn("text-xs font-medium", isToday && "text-primary")}>
                  {dayNameInZone(`${date}T12:00:00Z`, "UTC").slice(0, 3)}
                </div>
                <div className={cn("text-sm", isToday && "font-semibold text-primary")}>
                  {shortDateLabel(date)}
                </div>
              </div>
            );
          })}
        </div>

        {/* Body */}
        <div className="flex">
          {/* Hour gutter */}
          <div className="w-16 shrink-0 border-r">
            {hourLabels.map((h) => (
              <div
                key={h}
                className="relative border-b text-[11px] text-muted-foreground"
                style={{ height: HOUR_PX }}
              >
                <span className="absolute -top-2 right-2">{String(h).padStart(2, "0")}:00</span>
              </div>
            ))}
          </div>

          {dates.map((date) => {
            const dayName = dayNameInZone(`${date}T12:00:00Z`, "UTC");
            const windows = businessHours.filter((h) => h.dayOfWeek === dayName);
            const dayAppointments = byDate.get(date) ?? [];

            return (
              <div
                key={date}
                className="relative flex-1 border-r last:border-r-0"
                style={{ height: bodyHeight }}
              >
                {/* Hour lines */}
                {hourLabels.map((h) => (
                  <div key={h} className="border-b" style={{ height: HOUR_PX }} />
                ))}

                {/* Open-hours shading, so closed time is visibly different from merely empty time */}
                {windows.map((w) => {
                  const top = ((toMinutes(w.startLocal) - gridStartMin) / 60) * HOUR_PX;
                  const height = ((toMinutes(w.endLocal) - toMinutes(w.startLocal)) / 60) * HOUR_PX;
                  return (
                    <div
                      key={w.id}
                      className="pointer-events-none absolute inset-x-0 bg-muted/50"
                      style={{ top, height }}
                    />
                  );
                })}

                {/* Blackouts sit above opening hours — they override them */}
                {blackouts
                  .filter((b) => dateInZone(b.startUtc, timeZoneId) === date)
                  .map((b) => {
                    const top = offsetPx(b.startUtc);
                    const height = Math.max(
                      6,
                      (minutesBetween(b.startUtc, b.endUtc) / 60) * HOUR_PX
                    );
                    return (
                      <div
                        key={b.id}
                        title={b.reason}
                        className="pointer-events-none absolute inset-x-0 border-y border-dashed border-destructive/40 bg-[repeating-linear-gradient(45deg,transparent,transparent_5px,rgba(127,29,29,0.12)_5px,rgba(127,29,29,0.12)_10px)]"
                        style={{ top, height }}
                      />
                    );
                  })}

                {/* Appointments */}
                {dayAppointments.map((a) => {
                  const top = offsetPx(a.startUtc);
                  const height = Math.max(
                    22,
                    (minutesBetween(a.startUtc, a.endUtc) / 60) * HOUR_PX
                  );
                  return (
                    <button
                      key={a.id}
                      type="button"
                      onClick={() => onSelectAppointment(a.id)}
                      style={{ top, height }}
                      className={cn(
                        "absolute inset-x-1 overflow-hidden rounded-md border px-1.5 py-0.5 text-left text-[11px] leading-tight transition hover:brightness-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                        STATUS_STYLES[a.status] ?? STATUS_STYLES.Confirmed
                      )}
                    >
                      <div className="font-medium">{timeInZone(a.startUtc, timeZoneId)}</div>
                      <div className="truncate">{a.contactName}</div>
                      {height > 44 && (
                        <div className="truncate opacity-70">{a.appointmentTypeName}</div>
                      )}
                    </button>
                  );
                })}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
