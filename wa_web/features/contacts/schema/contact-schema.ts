import { z } from "zod";

/**
 * Create-contact form schema. Mirrors wa_api CreateContactRequest: Phone + Name required.
 * CompanyId is resolved server-side from the JWT — it is NEVER sent from the client.
 */
export const createContactSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Name is required")
    .max(200, "Name must be at most 200 characters"),
  phone: z
    .string()
    .trim()
    .min(5, "Phone number is required")
    .max(20, "Phone number must be at most 20 characters"),
  // Explicit opt-in consent — attests to off-platform proof. Sent as HasOptedIn to the API.
  hasOptedIn: z.boolean().optional(),
});

/**
 * Update schema — create fields plus a company-admin opt-out override.
 * `isOptedOut`: true = suppress ALL sending (mirrors a customer STOP), false = re-enable.
 * Omitted = leave unchanged. This is the only sanctioned way to lift a STOP.
 */
export const updateContactSchema = createContactSchema.extend({
  isOptedOut: z.boolean().optional(),
});

export type CreateContactInput = z.infer<typeof createContactSchema>;
export type UpdateContactInput = z.infer<typeof updateContactSchema>;
