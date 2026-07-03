"use server";

import { createAction } from "@/lib/actions/wrapper";
import { AccountService } from "@/features/account/services/account-service";
import { changePasswordSchema } from "@/features/account/schema/change-password-schema";

// Self-service, so requireAuth only (NO requiredRole) — every signed-in user may change
// their own password. logInputs is off: the field names aren't in the log-mask list.
export const changePasswordAction = createAction(
  { name: "changePasswordAction", requireAuth: true, logInputs: false },
  async (raw: unknown) => {
    const input = changePasswordSchema.parse(raw);
    return AccountService.changePassword(input);
  }
);
