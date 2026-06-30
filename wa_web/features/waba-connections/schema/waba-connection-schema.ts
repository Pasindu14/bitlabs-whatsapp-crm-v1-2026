import { z } from "zod";

/** WABA connection status — mirrors wa_api WabaConnectionStatus enum. */
export const WABA_CONNECTION_STATUSES = ["Connected", "Disconnected", "Invalid"] as const;
export type WabaConnectionStatus = (typeof WABA_CONNECTION_STATUSES)[number];

/**
 * Create-connection form schema. Mirrors wa_api CreateWabaConnectionRequest:
 * CompanyId / PhoneNumberId / WabaId / AccessToken required; DisplayPhoneNumber / Status optional.
 */
export const createWabaConnectionSchema = z.object({
  companyId: z.string().trim().min(1, "Select a company"),
  phoneNumberId: z
    .string()
    .trim()
    .min(1, "Phone number id is required")
    .max(64, "Phone number id must be at most 64 characters"),
  wabaId: z
    .string()
    .trim()
    .min(1, "WABA id is required")
    .max(64, "WABA id must be at most 64 characters"),
  displayPhoneNumber: z
    .string()
    .trim()
    .max(32, "Display phone number must be at most 32 characters")
    .optional()
    .or(z.literal("").transform(() => undefined)),
  accessToken: z
    .string()
    .trim()
    .min(1, "Access token is required")
    .max(2048, "Access token is too long"),
  appSecret: z
    .string()
    .trim()
    .min(1, "App secret is required")
    .max(512, "App secret is too long"),
  status: z.enum(WABA_CONNECTION_STATUSES).optional(),
});

/**
 * Update schema: same as create, but AccessToken is optional —
 * leave it blank to keep the stored token.
 */
export const updateWabaConnectionSchema = createWabaConnectionSchema.extend({
  accessToken: z
    .string()
    .trim()
    .max(2048, "Access token is too long")
    .optional()
    .or(z.literal("").transform(() => undefined)),
  appSecret: z
    .string()
    .trim()
    .max(512, "App secret is too long")
    .optional()
    .or(z.literal("").transform(() => undefined)),
});

export type CreateWabaConnectionInput = z.infer<typeof createWabaConnectionSchema>;
export type UpdateWabaConnectionInput = z.infer<typeof updateWabaConnectionSchema>;
