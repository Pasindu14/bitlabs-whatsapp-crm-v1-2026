import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { ChangePasswordInput } from "@/features/account/schema/change-password-schema";

/**
 * Talks to wa_api /api/v1/auth. The axios client attaches the caller's bearer token; the
 * change-password endpoint takes the account from that token (never the body) and works for
 * every role. Only the two API fields are sent — confirmPassword stays on the client.
 */
export const AccountService = {
  async changePassword(input: ChangePasswordInput): Promise<void> {
    return executeService(
      { context: "AccountService", method: "changePassword" },
      async () => {
        await client.post<ApiSuccessBody<{ changed: boolean }>>("/api/v1/auth/change-password", {
          currentPassword: input.currentPassword,
          newPassword: input.newPassword,
        });
      }
    );
  },
};
