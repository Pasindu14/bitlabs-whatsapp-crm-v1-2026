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
});

/** Update schema — same shape as create (kept separate so the two can diverge later). */
export const updateContactSchema = createContactSchema;

export type CreateContactInput = z.infer<typeof createContactSchema>;
export type UpdateContactInput = z.infer<typeof updateContactSchema>;
