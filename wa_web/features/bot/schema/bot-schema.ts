import { z } from "zod";
import { AUTO_REPLY_TRIGGERS, TERMLESS_TRIGGERS } from "@/features/bot/types";

/**
 * Create-rule form schema. Mirrors wa_api CreateAutoReplyRuleRequest.
 *
 * The cross-field rule matches AutoReplyRuleService.NormalizeTerms on the server: a
 * content trigger with no terms can never fire, so it is rejected here rather than
 * saved as a rule that silently does nothing. Welcome/Fallback ignore terms entirely.
 */
export const createAutoReplyRuleSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Rule name is required")
      .max(120, "Rule name must be at most 120 characters"),
    trigger: z.enum(AUTO_REPLY_TRIGGERS),
    terms: z.array(z.string().trim().min(1)).default([]),
    replyBody: z
      .string()
      .trim()
      .min(1, "Reply message is required")
      .max(4096, "Reply message must be at most 4096 characters"),
    priority: z.coerce
      .number({ message: "Priority is required" })
      .int("Priority must be a whole number")
      .min(0, "Priority cannot be negative")
      .max(10_000, "Priority is too large"),
    isEnabled: z.boolean().default(true),
  })
  .refine(
    (v) => TERMLESS_TRIGGERS.includes(v.trigger) || v.terms.length > 0,
    { path: ["terms"], message: "Add at least one trigger term" }
  );

export const updateAutoReplyRuleSchema = createAutoReplyRuleSchema;

export type CreateAutoReplyRuleInput = z.infer<typeof createAutoReplyRuleSchema>;
export type UpdateAutoReplyRuleInput = z.infer<typeof updateAutoReplyRuleSchema>;

// ── The AI assistant ──────────────────────────────────────────────────────

/**
 * Bot profile form schema. Mirrors wa_api SaveBotProfileRequest, including its lengths — the API
 * rejects an over-long field with a 400, and catching it here gives an inline message instead.
 */
export const saveBotProfileSchema = z.object({
  isEnabled: z.boolean().default(false),
  businessName: z
    .string()
    .trim()
    .min(1, "Business name is required")
    .max(200, "Business name must be at most 200 characters"),
  businessDescription: z
    .string()
    .trim()
    .min(1, "Tell the assistant what the business does")
    .max(2000, "Description must be at most 2000 characters"),
  toneNotes: z.string().trim().max(500, "Keep this under 500 characters").nullable().default(null),
  location: z.string().trim().max(500, "Keep this under 500 characters").nullable().default(null),
  contactNote: z.string().trim().max(500, "Keep this under 500 characters").nullable().default(null),
  bookingInstructions: z
    .string()
    .trim()
    .max(1000, "Keep this under 1000 characters")
    .nullable()
    .default(null),
  preferredLanguage: z.string().trim().max(60).nullable().default(null),
  currencyLabel: z.string().trim().max(10, "Use a short label like Rs. or LKR").nullable().default(null),
  dailyReplyLimit: z.coerce
    .number({ message: "Daily limit is required" })
    .int("Daily limit must be a whole number")
    .min(0, "Daily limit cannot be negative")
    .max(100_000, "Daily limit is too large"),
  followUpSteps: z
    .array(
      z.object({
        delayMinutes: z.coerce
          .number()
          .int()
          .min(1, "Wait at least a minute")
          .max(43_200, "Keep follow-ups within 30 days"),
        message: z
          .string()
          .trim()
          .min(1, "Write what this follow-up should say")
          .max(1000, "Keep this under 1000 characters"),
        templateId: z.string().nullable().default(null),
        // Steps saved before overrides existed come back as null, which must not fail validation.
        variableOverrides: z
          .array(z.string().trim())
          .max(10)
          .nullish()
          .transform((v) => v ?? []),
      })
    )
    .max(10, "At most 10 follow-ups")
    .default([]),
  resumeBotAfterIdleHours: z.coerce
    .number({ message: "Enter a number of hours" })
    .int("Use whole hours")
    .min(0, "Cannot be negative")
    .max(720, "Keep this under 30 days"),
  handoffAlertPhones: z.array(z.string()).default([]),
  handoffAlertTemplateId: z.string().nullable().default(null),
  handoffResumeMinutes: z.coerce
    .number({ message: "Enter a number of minutes" })
    .int("Use whole minutes")
    .min(0, "Cannot be negative")
    .max(10_080, "Keep this under 7 days"),
  // Always sent as a string, so clearing the box clears the rules (null tells the API to leave them).
  industryRules: z
    .string()
    .trim()
    .max(2000, "Keep this under 2000 characters")
    .nullable()
    .transform((v) => v ?? ""),
  bookingsEnabled: z.boolean().default(true),
});

export const saveBotFaqSchema = z.object({
  question: z
    .string()
    .trim()
    .min(1, "Question is required")
    .max(500, "Question must be at most 500 characters"),
  answer: z
    .string()
    .trim()
    .min(1, "Answer is required")
    .max(2000, "Answer must be at most 2000 characters"),
  sortOrder: z.coerce.number().int().min(0).default(0),
  isEnabled: z.boolean().default(true),
});

export const saveBotLinkSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, "Title is required")
    .max(100, "Title must be at most 100 characters"),
  // Sent to customers exactly as typed, so it has to be a full web address.
  url: z
    .string()
    .trim()
    .max(2000, "Link must be at most 2000 characters")
    .regex(/^https?:\/\/\S+$/i, "Enter a full link starting with https://"),
  whenToShare: z
    .string()
    .trim()
    .max(500, "Must be at most 500 characters")
    .nullable()
    .transform((v) => v || null),
  message: z
    .string()
    .trim()
    .max(300, "Message must be at most 300 characters")
    .nullable()
    .transform((v) => v || null),
  sortOrder: z.coerce.number().int().min(0).default(0),
  isEnabled: z.boolean().default(true),
});

export type SaveBotProfileInput = z.infer<typeof saveBotProfileSchema>;
export type SaveBotLinkInput = z.input<typeof saveBotLinkSchema>;
export type SaveBotFaqInput = z.infer<typeof saveBotFaqSchema>;
