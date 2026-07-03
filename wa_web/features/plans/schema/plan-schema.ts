import { z } from "zod";
import { PERMISSION_KEYS } from "@/features/team/permissions";

/**
 * Create-plan form schema. Mirrors wa_api CreatePlanRequest: Name required;
 * quota/price numeric; currency 3-letter; featureFlags a subset of the permission catalog.
 */
export const createPlanSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Plan name is required")
    .max(120, "Plan name must be at most 120 characters"),
  monthlyMessageQuota: z.coerce
    .number({ message: "Quota is required" })
    .int("Quota must be a whole number")
    .min(0, "Quota cannot be negative"),
  price: z.coerce
    .number({ message: "Price is required" })
    .min(0, "Price cannot be negative")
    .max(1_000_000, "Price is too large"),
  currency: z
    .string()
    .trim()
    .length(3, "Use a 3-letter currency code")
    .transform((v) => v.toUpperCase())
    .optional()
    .or(z.literal("").transform(() => undefined)),
  featureFlags: z.array(z.enum(PERMISSION_KEYS)).default([]),
  // Marks the plan as available for self-service online payment. Shown on the checkout
  // flow once online payments are configured; SuperAdmin-only metadata until then.
  isOnline: z.boolean().default(false),
});

/** Update schema is identical to create. */
export const updatePlanSchema = createPlanSchema;

export type CreatePlanInput = z.infer<typeof createPlanSchema>;
export type UpdatePlanInput = z.infer<typeof updatePlanSchema>;
