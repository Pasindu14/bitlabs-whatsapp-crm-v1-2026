import { z } from "zod";

/**
 * Create-list form schema. Mirrors wa_api CreateContactListRequest: Name required,
 * Description optional. CompanyId is resolved server-side from the JWT — never sent.
 */
export const createContactListSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Name is required")
    .max(120, "Name must be at most 120 characters"),
  description: z
    .string()
    .trim()
    .max(500, "Description must be at most 500 characters")
    .optional()
    .or(z.literal("").transform(() => undefined)),
});

/** Update schema — same shape as create. */
export const updateContactListSchema = createContactListSchema;

export type CreateContactListInput = z.infer<typeof createContactListSchema>;
export type UpdateContactListInput = z.infer<typeof updateContactListSchema>;
