import { z } from "zod";

/** Tenant roles a SuperAdmin can create/manage — mirrors wa_api UserRole (minus SuperAdmin). */
export const USER_ROLES = ["CompanyAdmin", "Agent"] as const;
export type UserRole = (typeof USER_ROLES)[number];

/**
 * Create-user form schema. Mirrors wa_api CreateUserRequest:
 * CompanyId / FullName / Email / Password required; Role optional (defaults to CompanyAdmin).
 */
export const createUserSchema = z.object({
  companyId: z.string().trim().min(1, "Select a company"),
  fullName: z
    .string()
    .trim()
    .min(2, "Full name must be at least 2 characters")
    .max(200, "Full name must be at most 200 characters"),
  email: z
    .string()
    .trim()
    .min(1, "Email is required")
    .email("Enter a valid email")
    .max(256, "Email is too long"),
  password: z
    .string()
    .min(8, "Password must be at least 8 characters")
    .max(128, "Password is too long"),
  role: z.enum(USER_ROLES).optional(),
});

/**
 * Update schema: identity + role only. The password is NOT editable here —
 * use the dedicated Reset Password flow ({@link resetPasswordSchema}).
 */
export const updateUserSchema = createUserSchema.omit({ password: true });

/** Reset-password form schema — a single new password. */
export const resetPasswordSchema = z.object({
  password: z
    .string()
    .min(8, "Password must be at least 8 characters")
    .max(128, "Password is too long"),
});

export type CreateUserInput = z.infer<typeof createUserSchema>;
export type UpdateUserInput = z.infer<typeof updateUserSchema>;
export type ResetPasswordInput = z.infer<typeof resetPasswordSchema>;
