"use client";

import { useState } from "react";
import { Copy, Plus, Save, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Spinner } from "@/components/ui/spinner";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { useSaveBusinessHours } from "@/features/calendar/hooks/use-calendar";
import { WEEK_DAYS, type BusinessHoursWindow, type DayName } from "@/features/calendar/types";

/** "09:00:00" from the API, "09:00" in an <input type="time"> — normalise for display. */
const toInput = (t: string) => t.slice(0, 5);

/** Minutes from midnight, for the weekly-total readout and for placing an added window. */
const toMinutes = (t: string) => {
  const [h, m] = t.split(":").map(Number);
  return h * 60 + (m || 0);
};

/** Inverse of {@link toMinutes} — "13:30" for 810. */
const fromMinutes = (mins: number) => {
  const clamped = Math.max(0, Math.min(mins, 23 * 60 + 59));
  return `${String(Math.floor(clamped / 60)).padStart(2, "0")}:${String(clamped % 60).padStart(2, "0")}`;
};

/** What a day gets when it is switched on, so a standard day is one click rather than three. */
const DEFAULT_WINDOW = { start: "09:00", end: "17:00" };

interface Row {
  /** Stable key for React across adds/removes; not sent to the API. */
  key: string;
  start: string;
  end: string;
}

type Draft = Record<DayName, Row[]>;

const emptyDraft = (): Draft => WEEK_DAYS.reduce((acc, d) => ({ ...acc, [d]: [] }), {} as Draft);

let seq = 0;
const newRow = (start = DEFAULT_WINDOW.start, end = DEFAULT_WINDOW.end): Row => ({
  key: `w${seq++}`,
  start,
  end,
});

function toDraft(windows: BusinessHoursWindow[]): Draft {
  const draft = emptyDraft();
  for (const w of windows) {
    draft[w.dayOfWeek].push({ key: w.id, start: toInput(w.startLocal), end: toInput(w.endLocal) });
  }
  for (const day of WEEK_DAYS) draft[day].sort((a, b) => a.start.localeCompare(b.start));
  return draft;
}

/** "40 hours", "37.5 hours", "1 hour" — the weekly total, rounded to the nearest half hour. */
function formatHours(minutes: number): string {
  const hours = Math.round((minutes / 60) * 2) / 2;
  return `${hours} ${hours === 1 ? "hour" : "hours"}`;
}

/**
 * The weekly opening schedule.
 *
 * A day is open or closed — a binary state, so it gets a switch, and switching one on seeds
 * 09:00–17:00 rather than making the operator build every day from nothing. Switching off keeps the
 * times aside so a mis-tap does not destroy typed input.
 *
 * Saved as replace-all: the whole grid is submitted at once, matching the API's semantics and keeping
 * the request idempotent. Times are the company's local wall clock, deliberately not UTC — "we open at
 * 09:00 on Mondays" must stay true across a daylight-saving change.
 */
export function BusinessHoursEditor({ windows }: { windows: BusinessHoursWindow[] }) {
  const save = useSaveBusinessHours();

  // Seeded once per mount. The parent keys this editor on the saved schedule, so a successful save
  // (or another operator's, on refetch) remounts it with fresh data instead of syncing via an effect.
  const [draft, setDraft] = useState<Draft>(() => toDraft(windows));
  // Times from a day that was switched off, kept so switching it back restores what was typed.
  const [stash, setStash] = useState<Partial<Record<DayName, Row[]>>>({});
  const [error, setError] = useState<string | null>(null);

  const setDay = (day: DayName, rows: Row[]) => setDraft((d) => ({ ...d, [day]: rows }));

  const toggleDay = (day: DayName, open: boolean) => {
    if (open) {
      const restored = stash[day]?.length ? stash[day]! : [newRow()];
      setDay(day, restored);
    } else {
      setStash((s) => ({ ...s, [day]: draft[day] }));
      setDay(day, []);
    }
  };

  /**
   * The card tells people two windows are how you leave a gap for lunch, so on a single long day that
   * is exactly what this does: split it at the midpoint with an hour between. Anything else appends a
   * window after the last one rather than landing on top of it. Both results stay fully editable.
   */
  const addRow = (day: DayName) => {
    const rows = draft[day];
    const last = rows.at(-1);

    if (rows.length === 1 && last && toMinutes(last.end) - toMinutes(last.start) >= 300) {
      const mid = Math.round((toMinutes(last.start) + toMinutes(last.end)) / 2 / 30) * 30;
      setDay(day, [
        { ...last, end: fromMinutes(mid - 30) },
        newRow(fromMinutes(mid + 30), last.end),
      ]);
      return;
    }

    const start = last ? Math.min(toMinutes(last.end) + 60, 22 * 60) : toMinutes(DEFAULT_WINDOW.start);
    setDay(day, [...rows, newRow(fromMinutes(start), fromMinutes(Math.min(start + 180, 23 * 60 + 30)))]);
  };
  const removeRow = (day: DayName, key: string) =>
    setDay(day, draft[day].filter((r) => r.key !== key));
  const setRow = (day: DayName, key: string, patch: Partial<Row>) =>
    setDay(day, draft[day].map((r) => (r.key === key ? { ...r, ...patch } : r)));

  /** Most businesses repeat one pattern, so copying beats retyping it six times. */
  const copyToAllDays = (from: DayName) => {
    const source = draft[from];
    setDraft((d) => {
      const next = { ...d };
      for (const day of WEEK_DAYS) {
        next[day] = source.map((r) => newRow(r.start, r.end));
      }
      return next;
    });
    setError(null);
  };

  const openDays = WEEK_DAYS.filter((day) => draft[day].length > 0);
  const totalMinutes = WEEK_DAYS.reduce(
    (sum, day) =>
      sum + draft[day].reduce((s, r) => s + Math.max(0, toMinutes(r.end) - toMinutes(r.start)), 0),
    0
  );

  const submit = () => {
    // Mirrors the API's BUSINESS_HOURS_INVALID rule so the operator sees it before the round-trip.
    for (const day of WEEK_DAYS) {
      for (const r of draft[day]) {
        if (r.end <= r.start) {
          setError(`${day} closes at or before it opens. Check that day's times.`);
          return;
        }
      }
    }
    setError(null);

    save.mutate(
      WEEK_DAYS.flatMap((day) =>
        draft[day].map((r) => ({ dayOfWeek: day, startLocal: r.start, endLocal: r.end }))
      )
    );
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Opening hours</CardTitle>
        <CardDescription>
          When customers can book. Add two windows on a day to leave a gap for lunch — the gap is not
          bookable. A day with no windows is closed.
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        {/* One divided list rather than seven bordered boxes: less chrome, half the height, and the
            week's pattern reads straight down the column. */}
        <div className="divide-y overflow-hidden rounded-lg border">
          {WEEK_DAYS.map((day) => {
            const rows = draft[day];
            const isOpen = rows.length > 0;

            return (
              <div
                key={day}
                className={cn(
                  "flex flex-col gap-2 px-4 py-3 transition-colors sm:flex-row sm:items-center sm:gap-4",
                  !isOpen && "bg-muted/30"
                )}
              >
                <div className="flex items-center gap-3 sm:w-40 sm:shrink-0">
                  <Switch
                    checked={isOpen}
                    onCheckedChange={(v) => toggleDay(day, v)}
                    aria-label={`${day} open`}
                  />
                  <span
                    className={cn(
                      "text-sm",
                      isOpen ? "font-medium" : "text-muted-foreground"
                    )}
                  >
                    {day}
                  </span>
                </div>

                {/* pl-11 on phones lines the windows up under the day name, past the switch. */}
                <div className="flex flex-1 flex-wrap items-center gap-2 pl-11 sm:pl-0">
                  {!isOpen ? (
                    <span className="text-sm text-muted-foreground">Closed</span>
                  ) : (
                    <>
                      {rows.map((r, i) => (
                        <div
                          key={r.key}
                          // Each start/end pair sits on its own chip so the two stay visually joined
                          // once a second window wraps onto another line.
                          className="flex w-full items-center gap-1.5 rounded-md bg-muted/60 px-2 py-1 sm:w-auto"
                        >
                          <Input
                            type="time"
                            value={r.start}
                            onChange={(e) => setRow(day, r.key, { start: e.target.value })}
                            // 136px is what the native control needs for "09:00 AM" plus its clock
                            // button in a 12-hour locale; below sm the pair splits the row instead.
                            className="h-8 min-w-0 flex-1 border-0 bg-background sm:w-[136px] sm:flex-none"
                            aria-label={`${day} window ${i + 1} opens`}
                          />
                          <span className="shrink-0 text-xs text-muted-foreground">to</span>
                          <Input
                            type="time"
                            value={r.end}
                            onChange={(e) => setRow(day, r.key, { end: e.target.value })}
                            className="h-8 min-w-0 flex-1 border-0 bg-background sm:w-[136px] sm:flex-none"
                            aria-label={`${day} window ${i + 1} closes`}
                          />
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="size-6 shrink-0 text-muted-foreground hover:text-foreground"
                            aria-label={`Remove ${day} window ${i + 1}`}
                            onClick={() => removeRow(day, r.key)}
                          >
                            <X className="size-3.5" />
                          </Button>
                        </div>
                      ))}

                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-8 shrink-0 text-muted-foreground hover:text-foreground"
                        onClick={() => addRow(day)}
                      >
                        <Plus className="size-3.5" /> Add window
                      </Button>

                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-8 shrink-0 text-muted-foreground hover:text-foreground"
                        onClick={() => copyToAllDays(day)}
                      >
                        <Copy className="size-3.5" /> Copy to all days
                      </Button>
                    </>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        {error && <p className="text-sm text-destructive">{error}</p>}

        {/* The summary earns the footer its space: it confirms the week you just described, and
            catches a 09:00–07:00 typo that would silently make a day unbookable. */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground">
            {openDays.length === 0
              ? "Closed all week. Switch on a day to start taking bookings."
              : `${openDays.length} ${openDays.length === 1 ? "day" : "days"} open, ${formatHours(totalMinutes)} a week`}
          </p>

          <Button type="button" onClick={submit} disabled={save.isPending}>
            {save.isPending ? <Spinner className="size-4" /> : <Save className="size-4" />}
            Save opening hours
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
