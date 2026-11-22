import { z } from "zod";

/**
 * Form schemas for the calendar module. Every rule here mirrors a wa_api DataAnnotation so the
 * client rejects what the API would reject anyway — the API stays the authority, this just
 * saves a round-trip and gives field-level messages.
 */

const HHMM = /^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$/;

const dayName = z.enum([
  "Sunday",
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
]);

// ── Appointment type ───────────────────────────────────────────────────────

export const appointmentTypeSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Name is required")
      .max(120, "Name must be at most 120 characters"),
    description: z.string().trim().max(500, "Description must be at most 500 characters").optional(),
    durationMinutes: z.coerce
      .number()
      .int("Must be a whole number of minutes")
      .min(5, "Minimum 5 minutes")
      .max(1440, "Maximum 24 hours"),
    slotIntervalMinutes: z.coerce
      .number()
      .int("Must be a whole number of minutes")
      .min(5, "Minimum 5 minutes")
      .max(1440, "Maximum 24 hours"),
    bufferBeforeMinutes: z.coerce.number().int().min(0).max(480, "Maximum 8 hours"),
    bufferAfterMinutes: z.coerce.number().int().min(0).max(480, "Maximum 8 hours"),
    minimumNoticeMinutes: z.coerce.number().int().min(0).max(43200, "Maximum 30 days"),
    bookingHorizonDays: z.coerce
      .number()
      .int()
      .min(1, "Must offer at least one day")
      .max(365, "Maximum one year"),
    price: z.coerce.number().min(0, "Price cannot be negative").optional(),
    isEnabled: z.boolean().optional(),
  });

export type AppointmentTypeInput = z.infer<typeof appointmentTypeSchema>;

// ── Business hours ─────────────────────────────────────────────────────────

export const businessHoursWindowSchema = z
  .object({
    dayOfWeek: dayName,
    startLocal: z.string().regex(HHMM, "Use HH:MM"),
    endLocal: z.string().regex(HHMM, "Use HH:MM"),
  })
  // Mirrors the API's BUSINESS_HOURS_INVALID rule. A window crossing midnight is entered as two
  // windows on two days, so the calculator never sees one spanning a date boundary.
  .refine((w) => w.endLocal > w.startLocal, {
    message: "Closing time must be after opening time",
    path: ["endLocal"],
  });

export const saveBusinessHoursSchema = z.object({
  windows: z.array(businessHoursWindowSchema),
});

export type BusinessHoursWindowInput = z.infer<typeof businessHoursWindowSchema>;
export type SaveBusinessHoursInput = z.infer<typeof saveBusinessHoursSchema>;

// ── Timezone ───────────────────────────────────────────────────────────────

export const timeZoneSchema = z.object({
  timeZoneId: z
    .string()
    .trim()
    .min(1, "Pick a timezone")
    .max(64, "Timezone id must be at most 64 characters"),
});

export type TimeZoneInput = z.infer<typeof timeZoneSchema>;

// ── Blackout ───────────────────────────────────────────────────────────────

export const blackoutSchema = z
  .object({
    startUtc: z.string().min(1, "Start is required"),
    endUtc: z.string().min(1, "End is required"),
    reason: z
      .string()
      .trim()
      .min(1, "Reason is required")
      .max(200, "Reason must be at most 200 characters"),
  })
  .refine((b) => new Date(b.endUtc) > new Date(b.startUtc), {
    message: "The end must be after the start",
    path: ["endUtc"],
  });

export type BlackoutInput = z.infer<typeof blackoutSchema>;

// ── Appointment ────────────────────────────────────────────────────────────

export const createAppointmentSchema = z.object({
  contactId: z.string().min(1, "Choose a contact"),
  appointmentTypeId: z.string().min(1, "Choose a service"),
  // ISO 8601 UTC. Only the start is sent — the API derives the end from the service's duration.
  startUtc: z.string().min(1, "Choose a time"),
  notes: z.string().trim().max(1000, "Notes must be at most 1000 characters").optional(),
  conversationId: z.string().optional(),
});

export const cancelAppointmentSchema = z.object({
  reason: z.string().trim().max(500, "Reason must be at most 500 characters").optional(),
});

export type CreateAppointmentInput = z.infer<typeof createAppointmentSchema>;
export type CancelAppointmentInput = z.infer<typeof cancelAppointmentSchema>;
