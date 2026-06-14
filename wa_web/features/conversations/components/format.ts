import { format, isToday, isYesterday, parseISO } from "date-fns";

/** API timestamps are UTC ISO strings (with a Z); parseISO + format render them in the viewer's zone. */

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

/** Inbox-row time: today → HH:mm, yesterday → "Yesterday", otherwise dd MMM. */
export function formatListTime(iso: string): string {
  const d = parseISO(iso);
  if (isToday(d)) return format(d, "HH:mm");
  if (isYesterday(d)) return "Yesterday";
  return format(d, "dd MMM");
}

export function formatMessageTime(iso: string): string {
  return format(parseISO(iso), "HH:mm");
}

export function formatDayDivider(iso: string): string {
  const d = parseISO(iso);
  if (isToday(d)) return "Today";
  if (isYesterday(d)) return "Yesterday";
  return format(d, "EEEE, dd MMM yyyy");
}

/** Same calendar day? Used to group messages under day dividers. */
export function sameDay(a: string, b: string): boolean {
  return format(parseISO(a), "yyyy-MM-dd") === format(parseISO(b), "yyyy-MM-dd");
}

export function displayPhone(phone: string): string {
  return phone.startsWith("+") ? phone : `+${phone}`;
}
