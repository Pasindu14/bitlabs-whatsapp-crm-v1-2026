import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";

/** Mirrors wa_api BotAccessResponse. */
export interface BotAccess {
  /** The platform master switch. Off means the bot answers nobody, whatever the plan says. */
  platformEnabled: boolean;
  /** The caller's plan includes the chatbot. */
  chatbot: boolean;
  /** The caller's plan includes the calendar. */
  calendar: boolean;
}

export const NO_BOT_ACCESS: BotAccess = { platformEnabled: false, chatbot: false, calendar: false };

/** Talks to wa_api /api/v1/bot/access — readable by any signed-in tenant user, never plan-gated. */
export const BotAccessService = {
  async get(): Promise<BotAccess> {
    return executeService({ context: "BotAccessService", method: "get" }, async () => {
      const res = await client.get<ApiSuccessBody<BotAccess>>("/api/v1/bot/access");
      return res.data.data ?? NO_BOT_ACCESS;
    });
  },
};
