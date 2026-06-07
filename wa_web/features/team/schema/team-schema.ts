import { z } from "zod";
import { PERMISSION_KEYS } from "@/features/team/permissions";

/** Roles a CompanyAdmin can create/manage — mirrors wa_api UserRole (minus SuperAdmin). */
export const TEAM_ROLES = ["CompanyAdmin", "Agent"] as const;
export type TeamRole = (typeof TEAM_ROLES)[number];

/**
 * Create-member form schema. Mirrors wa_api CreateCompanyUserRequest:
 * FullName / Email / Password required; Role optional (defaults to Agent).
 * There is NO companyId — the API derives the company from the JWT.
 */
export const createTeamMemberSchema = z.object({
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
  role: z.enum(TEAM_ROLES).optional(),
  // Capability grants — only applied for Agents (the API ignores them for CompanyAdmins).
  permissions: z.array(z.enum(PERMISSION_KEYS)).optional(),
});

/**
 * Update schema: identity + role only. The password is NOT editable here —
 * use the dedicated Reset Password flow ({@link resetTeamPasswordSchema}).
 */
export const updateTeamMemberSchema = createTeamMemberSchema.omit({ password: true });

/** Reset-password form schema — a single new password. */
export const resetTeamPasswordSchema = z.object({
  password: z
    .string()
    .min(8, "Password must be at least 8 characters")
    .max(128, "Password is too long"),
});

export type CreateTeamMemberInput = z.infer<typeof createTeamMemberSchema>;
export type UpdateTeamMemberInput = z.infer<typeof updateTeamMemberSchema>;
export type ResetTeamPasswordInput = z.infer<typeof resetTeamPasswordSchema>;
