/**
 * Timezone helpers for the calendar screens.
 *
 * Everything the API stores is UTC, but the calendar must be read and edited in the COMPANY's zone —
 * not the viewer's. An operator in London administering a Colombo business needs to see Colombo's
 * Monday morning, so none of these use the browser's local zone.
 */

/** Weekday names in .NET's DayOfWeek serialization order (Sunday = 0). */
const DAY_NAMES = [
  "Sunday",
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
] as const;

/**
 * The offset of `tz` from UTC at a given instant, in milliseconds (east of UTC is positive).
 *
 * Derived by formatting the instant into the target zone and reading the wall-clock back, which is the
 * only way to get a zone's offset from the platform Intl data without shipping a tzdb.
 */
function zoneOffsetMs(instant: Date, tz: string): number {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: tz,
    hour12: false,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).formatToParts(instant);

  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? "0");

  // Intl renders midnight as hour 24 in some engines; normalise so Date.UTC does not roll the day.
  const hour = get("hour") % 24;

  const asUtc = Date.UTC(
    get("year"),
    get("month") - 1,
    get("day"),
    hour,
    get("minute"),
    get("second")
  );

  return asUtc - instant.getTime();
}

/**
 * Converts a wall-clock time in `tz` to the UTC instant it denotes.
 *
 * Two passes: the first guesses the offset by reading it at the naive instant, the second re-reads it
 * at the corrected instant. That second pass is what makes times near a DST transition land correctly —
 * the offset there differs on either side of the guess.
 *
 * @param date "YYYY-MM-DD" in the company's zone
 * @param time "HH:MM" (or "HH:MM:SS") in the company's zone
 */
export function zonedToUtc(date: string, time: string, tz: string): Date {
  const hhmmss = time.length === 5 ? `${time}:00` : time;
  const naive = new Date(`${date}T${hhmmss}Z`);

  const firstPass = new Date(naive.getTime() - zoneOffsetMs(naive, tz));
  return new Date(naive.getTime() - zoneOffsetMs(firstPass, tz));
}

/** "YYYY-MM-DD" for an instant, as that date reads in `tz`. */
export function dateInZone(instant: Date | string, tz: string): string {
  const d = typeof instant === "string" ? new Date(instant) : instant;
  // en-CA renders ISO-shaped dates, which is exactly the format the API's date params want.
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: tz,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(d);
}

/** "09:30" for an instant, as the clock reads in `tz`. */
export function timeInZone(instant: Date | string, tz: string): string {
  const d = typeof instant === "string" ? new Date(instant) : instant;
  return new Intl.DateTimeFormat("en-GB", {
    timeZone: tz,
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(d);
}

/** "Mon 1 Jun, 09:30" — the label used on appointment cards and confirmations. */
export function dateTimeInZone(instant: Date | string, tz: string): string {
  const d = typeof instant === "string" ? new Date(instant) : instant;
  return new Intl.DateTimeFormat("en-GB", {
    timeZone: tz,
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(d);
}

/** The weekday an instant falls on in `tz`, named as the API names it. */
export function dayNameInZone(instant: Date | string, tz: string): string {
  const d = typeof instant === "string" ? new Date(instant) : instant;
  const short = new Intl.DateTimeFormat("en-US", { timeZone: tz, weekday: "long" }).format(d);
  return DAY_NAMES.find((n) => n === short) ?? short;
}

/** "YYYY-MM-DD" of today, as that date reads in `tz`. */
export function todayInZone(tz: string): string {
  return dateInZone(new Date(), tz);
}

/** Shifts an ISO date string by whole days. Pure string/UTC arithmetic — no zone involved. */
export function addDays(isoDate: string, days: number): string {
  const d = new Date(`${isoDate}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** The Monday of the week containing `isoDate`, as a "YYYY-MM-DD" string. */
export function startOfWeek(isoDate: string): string {
  const d = new Date(`${isoDate}T00:00:00Z`);
  // getUTCDay: Sunday = 0. Shift so Monday is the first column, which is how a working week reads.
  const shift = (d.getUTCDay() + 6) % 7;
  return addDays(isoDate, -shift);
}

/** The seven "YYYY-MM-DD" dates of the week containing `isoDate`, Monday first. */
export function weekDates(isoDate: string): string[] {
  const monday = startOfWeek(isoDate);
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i));
}

/** "1 Jun" — column header for a day in the grid. */
export function shortDateLabel(isoDate: string): string {
  return new Intl.DateTimeFormat("en-GB", {
    timeZone: "UTC", // the string is already a plain date; render it literally
    day: "numeric",
    month: "short",
  }).format(new Date(`${isoDate}T00:00:00Z`));
}

/** Minutes between two instants — used to size an appointment block in the grid. */
export function minutesBetween(startIso: string, endIso: string): number {
  return (new Date(endIso).getTime() - new Date(startIso).getTime()) / 60000;
}

/** Minutes from midnight in `tz` — the vertical position of an instant in the day grid. */
export function minutesFromMidnight(instant: string, tz: string): number {
  const [h, m] = timeInZone(instant, tz).split(":").map(Number);
  return h * 60 + m;
}
