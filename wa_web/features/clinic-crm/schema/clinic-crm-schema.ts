import { z } from "zod";

/**
 * Connection form. Mirrors wa_api SaveClinicCrmConnectionRequest.
 *
 * The secret is optional here because the API never returns it: an empty field on an existing
 * connection means "keep the saved one". The API itself insists on it for a new connection.
 */
export const saveClinicCrmConnectionSchema = z.object({
  baseUrl: z
    .string()
    .trim()
    .min(1, "API address is required")
    .max(500, "API address is too long")
    .regex(/^https?:\/\/\S+$/i, "Enter the full address, starting with http:// or https://"),
  clientId: z.string().trim().min(1, "Client ID is required").max(200, "Client ID is too long"),
  clientSecret: z
    .string()
    .trim()
    .max(2048, "Client secret is too long")
    .transform((v) => (v.length === 0 ? null : v))
    .nullable()
    .optional(),
  isEnabled: z.boolean().default(true),
  notifyOnMessages: z.boolean().default(false),
});

export type SaveClinicCrmConnectionInput = z.input<typeof saveClinicCrmConnectionSchema>;

/** Mirrors wa_api SaveClinicCrmMappingsRequest. */
export const saveClinicCrmMappingsSchema = z.object({
  items: z.array(
    z.object({
      appointmentTypeId: z.string().uuid(),
      externalTreatmentId: z.number().int().positive().nullable(),
    })
  ),
});

export type SaveClinicCrmMappingsInput = z.infer<typeof saveClinicCrmMappingsSchema>;
