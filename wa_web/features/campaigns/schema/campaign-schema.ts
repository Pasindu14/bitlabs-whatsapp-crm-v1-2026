import { z } from "zod";

// UAE marketing quiet-hours: sends are only permitted 08:00–20:00 Asia/Dubai (UTC+4, no DST).
// The backend is authoritative; this mirrors it so a bad time is caught before submit.
const UAE_OPEN_MIN = 8 * 60;
const UAE_CLOSE_MIN = 20 * 60;

/** Minutes-since-midnight of a UTC ISO instant in UAE local time. */
function uaeMinutesOfDay(iso: string): number {
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Asia/Dubai",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(new Date(iso));
  const hh = Number(parts.find((p) => p.type === "hour")?.value ?? "0");
  const mm = Number(parts.find((p) => p.type === "minute")?.value ?? "0");
  return hh * 60 + mm;
}

export const createCampaignSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Name is required")
      .max(200, "Name must be at most 200 characters"),
    templateId: z.string().uuid("Please select a template"),
    contactListIds: z.array(z.string().uuid()).optional(),
    contactIds: z.array(z.string().uuid()).optional(),
    variableMapping: z.string().optional(),
    scheduleType: z.enum(["Immediate", "OneTime", "Recurring"]),
    // datetime-local gives "YYYY-MM-DDTHH:mm" interpreted as the user's LOCAL wall-clock time.
    // `new Date(local).toISOString()` converts it to the equivalent UTC instant the API stores,
    // so a Sri Lanka user picking 3:31 PM schedules the send for 3:31 PM their time (10:01 UTC).
    scheduledAt: z
      .string()
      .regex(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/, "Invalid date/time format")
      .transform((v) => new Date(v).toISOString())
      .optional()
      .nullable(),
    recurrenceCron: z
      .string()
      .max(120)
      .optional()
      .nullable(),
    // Send to contacts without recorded opt-in (bypasses the NO_CONSENT skip gate). Defaults off.
    overrideConsentGate: z.boolean().optional(),
  })
  .refine(
    (d) =>
      (d.contactListIds && d.contactListIds.length > 0) ||
      (d.contactIds && d.contactIds.length > 0),
    { message: "Select at least one contact list or individual contact", path: ["contactListIds"] }
  )
  .refine(
    (d) => d.scheduleType !== "OneTime" || !!d.scheduledAt,
    { message: "Schedule date/time is required for one-time sends", path: ["scheduledAt"] }
  )
  .refine(
    (d) => d.scheduleType !== "Recurring" || !!d.recurrenceCron,
    { message: "Cron expression is required for recurring sends", path: ["recurrenceCron"] }
  )
  .refine(
    (d) => {
      if (d.scheduleType !== "OneTime" || !d.scheduledAt) return true;
      const m = uaeMinutesOfDay(d.scheduledAt);
      return m >= UAE_OPEN_MIN && m <= UAE_CLOSE_MIN;
    },
    { message: "Sends are only allowed between 8:00 AM and 8:00 PM UAE time", path: ["scheduledAt"] }
  );

export const updateCampaignSchema = createCampaignSchema;

export type CreateCampaignInput = z.infer<typeof createCampaignSchema>;
export type UpdateCampaignInput = z.infer<typeof updateCampaignSchema>;
