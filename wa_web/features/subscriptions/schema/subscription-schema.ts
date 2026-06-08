import { z } from "zod";

/** Assign-subscription form schema. Mirrors wa_api AssignSubscriptionRequest. */
export const assignSubscriptionSchema = z.object({
  companyId: z.string().trim().min(1, "Select a company"),
  planId: z.string().trim().min(1, "Select a plan"),
  periodDays: z.coerce
    .number()
    .int("Period must be a whole number of days")
    .min(1, "At least 1 day")
    .max(366, "At most 366 days")
    .optional(),
});

/** Change-plan form schema. Mirrors wa_api ChangePlanRequest. */
export const changePlanSchema = z.object({
  planId: z.string().trim().min(1, "Select a plan"),
  resetPeriod: z.boolean().optional(),
  periodDays: z.coerce
    .number()
    .int("Period must be a whole number of days")
    .min(1, "At least 1 day")
    .max(366, "At most 366 days")
    .optional(),
});

export type AssignSubscriptionInput = z.infer<typeof assignSubscriptionSchema>;
export type ChangePlanInput = z.infer<typeof changePlanSchema>;
