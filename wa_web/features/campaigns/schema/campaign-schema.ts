import { z } from "zod";

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
    scheduledAt: z.string().optional().nullable(),
    recurrenceCron: z
      .string()
      .max(120)
      .optional()
      .nullable(),
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
  );

export const updateCampaignSchema = createCampaignSchema;

export type CreateCampaignInput = z.infer<typeof createCampaignSchema>;
export type UpdateCampaignInput = z.infer<typeof updateCampaignSchema>;
