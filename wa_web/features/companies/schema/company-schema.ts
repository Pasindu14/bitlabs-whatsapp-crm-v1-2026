import { z } from "zod";

/**
 * Create-company form schema. Mirrors wa_api CreateCompanyRequest:
 * Name required; Slug/Email/Phone optional. Empty strings are normalised to undefined.
 */
export const createCompanySchema = z.object({
  name: z
    .string()
    .trim()
    .min(2, "Name must be at least 2 characters")
    .max(200, "Name must be at most 200 characters"),
  slug: z
    .string()
    .trim()
    .max(120, "Slug must be at most 120 characters")
    .optional()
    .or(z.literal("").transform(() => undefined)),
  email: z
    .string()
    .trim()
    .email("Enter a valid email")
    .max(256)
    .optional()
    .or(z.literal("").transform(() => undefined)),
  phone: z
    .string()
    .trim()
    .max(32, "Phone must be at most 32 characters")
    .optional()
    .or(z.literal("").transform(() => undefined)),
});

// Update uses the same shape as create (full replace of editable fields).
export const updateCompanySchema = createCompanySchema;

export type CreateCompanyInput = z.infer<typeof createCompanySchema>;
export type UpdateCompanyInput = z.infer<typeof updateCompanySchema>;
