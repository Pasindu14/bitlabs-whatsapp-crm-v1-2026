import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import { NO_BOT_ACCESS, type BotAccess } from "@/features/bot/access";

/**
 * SERVER ONLY — imports the API client. Client components must take BotAccess / NO_BOT_ACCESS from
 * "@/features/bot/access", never from here.
 *
 * Talks to wa_api /api/v1/bot/access — readable by any signed-in tenant user, never plan-gated. */
export const BotAccessService = {
  async get(): Promise<BotAccess> {
    return executeService({ context: "BotAccessService", method: "get" }, async () => {
      const res = await client.get<ApiSuccessBody<BotAccess>>("/api/v1/bot/access");
      return res.data.data ?? NO_BOT_ACCESS;
    });
  },
};
