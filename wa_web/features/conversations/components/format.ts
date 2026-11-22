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

/**
 * A stable soft colour per contact, so the same person is recognisable at a glance in the list and
 * the thread. Picked from the name (or number) so it never changes between renders.
 */
const AVATAR_TONES = [
  "bg-[#dcf1e3] text-[#0f6b3e]",
  "bg-[#dcebfa] text-[#1b5e9e]",
  "bg-[#fbe7d3] text-[#9a4f0f]",
  "bg-[#ede3fa] text-[#5b3a9e]",
  "bg-[#fadce4] text-[#9e2b4b]",
  "bg-[#d9f2ef] text-[#0f6b66]",
] as const;

export function avatarTone(seed: string): string {
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) | 0;
  return AVATAR_TONES[Math.abs(h) % AVATAR_TONES.length];
}

/** Time left in the 24-hour customer service window: "18h left", "40m left", or null when closed. */
export function windowLeft(expiresAt: string | null): string | null {
  if (!expiresAt) return null;
  const mins = Math.floor((new Date(expiresAt).getTime() - Date.now()) / 60_000);
  if (mins <= 0) return null;
  if (mins < 60) return `${mins}m left`;
  return `${Math.floor(mins / 60)}h left`;
}
