/** Small formatting helpers shared by the dashboard widgets. No server imports. */

export function initials(source: string): string {
  const parts = (source || "").trim().split(/\s+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return (parts[0]?.replace(/^\+/, "").slice(0, 2) || "··").toUpperCase();
}

/** Whole-number percentage of part/total, capped at 100. Null when there is nothing to divide by. */
export function pct(part: number, total: number): number | null {
  if (!total) return null;
  return Math.min(100, Math.round((part / total) * 100));
}

/** Percent change from previous to current. Null when there is no previous baseline. */
export function change(current: number, previous: number): number | null {
  if (!previous) return null;
  return ((current - previous) / previous) * 100;
}

const dateFmt = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric" });

/** "now", "5m", "3h", "2d", then a short date. */
export function timeAgo(iso: string): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "";
  const mins = Math.max(0, Math.round((Date.now() - then) / 60_000));
  if (mins < 1) return "now";
  if (mins < 60) return `${mins}m`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days}d`;
  return dateFmt.format(new Date(iso));
}
