import { z } from "zod";

/**
 * Create-package form schema. Mirrors wa_api CreatePackageRequest: Name required;
 * extraMessages a positive whole number; price numeric; currency 3-letter.
 */
export const createPackageSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Package name is required")
    .max(120, "Package name must be at most 120 characters"),
  description: z
    .string()
    .trim()
    .max(500, "Description must be at most 500 characters")
    .optional()
    .or(z.literal("").transform(() => undefined)),
  extraMessages: z.coerce
    .number({ message: "Extra messages is required" })
    .int("Must be a whole number")
    .min(1, "Must grant at least 1 message"),
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
});

/** Update schema is identical to create. */
export const updatePackageSchema = createPackageSchema;

export type CreatePackageInput = z.infer<typeof createPackageSchema>;
export type UpdatePackageInput = z.infer<typeof updatePackageSchema>;
