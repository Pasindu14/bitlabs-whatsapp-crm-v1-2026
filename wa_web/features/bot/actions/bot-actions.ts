"use server";

import { createAction } from "@/lib/actions/wrapper";
import { AutoReplyRuleService, BotProfileService } from "@/features/bot/services/bot-service";
import type { BotPreviewTurn } from "@/features/bot/types";
import {
  createAutoReplyRuleSchema,
  updateAutoReplyRuleSchema,
  saveBotFaqSchema,
  saveBotLinkSchema,
  saveBotProfileSchema,
} from "@/features/bot/schema/bot-schema";

// The API gates these on the manage_bot permission, which CompanyAdmin holds implicitly.
// The wrapper's role check is defense-in-depth on top of that.
const COMPANY_ADMIN = "CompanyAdmin";

export const getAutoReplyRulesAction = createAction(
  { name: "getAutoReplyRulesAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => AutoReplyRuleService.getAll()
);

export const getAutoReplyRuleByIdAction = createAction(
  { name: "getAutoReplyRuleByIdAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => AutoReplyRuleService.getById(id)
);

export const createAutoReplyRuleAction = createAction(
  { name: "createAutoReplyRuleAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = createAutoReplyRuleSchema.parse(raw);
    return AutoReplyRuleService.create(input);
  }
);

export const updateAutoReplyRuleAction = createAction(
  { name: "updateAutoReplyRuleAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = updateAutoReplyRuleSchema.parse(raw);
    return AutoReplyRuleService.update(id, input);
  }
);

export const deleteAutoReplyRuleAction = createAction(
  { name: "deleteAutoReplyRuleAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => AutoReplyRuleService.remove(id)
);

// ── The AI assistant ──────────────────────────────────────────────────────

export const getBotProfileAction = createAction(
  { name: "getBotProfileAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => BotProfileService.get()
);

export const saveBotProfileAction = createAction(
  { name: "saveBotProfileAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = saveBotProfileSchema.parse(raw);
    return BotProfileService.save(input);
  }
);

export const getBotFaqsAction = createAction(
  { name: "getBotFaqsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => BotProfileService.getFaqs()
);

export const createBotFaqAction = createAction(
  { name: "createBotFaqAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = saveBotFaqSchema.parse(raw);
    return BotProfileService.createFaq(input);
  }
);

export const updateBotFaqAction = createAction(
  { name: "updateBotFaqAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = saveBotFaqSchema.parse(raw);
    return BotProfileService.updateFaq(id, input);
  }
);

export const deleteBotFaqAction = createAction(
  { name: "deleteBotFaqAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => BotProfileService.removeFaq(id)
);

export const getBotLinksAction = createAction(
  { name: "getBotLinksAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => BotProfileService.getLinks()
);

export const createBotLinkAction = createAction(
  { name: "createBotLinkAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = saveBotLinkSchema.parse(raw);
    return BotProfileService.createLink(input);
  }
);

export const updateBotLinkAction = createAction(
  { name: "updateBotLinkAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = saveBotLinkSchema.parse(raw);
    return BotProfileService.updateLink(id, input);
  }
);

export const deleteBotLinkAction = createAction(
  { name: "deleteBotLinkAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => BotProfileService.removeLink(id)
);

export const getBotPromptAction = createAction(
  { name: "getBotPromptAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => BotProfileService.getPrompt()
);

export const previewBotAction = createAction(
  { name: "previewBotAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (turns: BotPreviewTurn[]) => BotProfileService.preview(turns)
);
