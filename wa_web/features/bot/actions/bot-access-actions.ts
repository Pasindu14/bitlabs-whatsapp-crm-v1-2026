"use server";

import { createAction } from "@/lib/actions/wrapper";
import { BotAccessService } from "@/features/bot/services/bot-access-service";

// Any signed-in tenant user: the sidebar and the inbox both need to know whether to show chatbot controls.
export const getBotAccessAction = createAction(
  { name: "getBotAccessAction", requireAuth: true },
  async () => BotAccessService.get()
);
