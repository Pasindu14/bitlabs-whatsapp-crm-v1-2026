import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type {
  AutoReplyRule,
  BotFaq,
  BotLink,
  BotPreviewResult,
  BotPrompt,
  BotPreviewTurn,
  BotProfile,
} from "@/features/bot/types";
import type {
  CreateAutoReplyRuleInput,
  UpdateAutoReplyRuleInput,
  SaveBotFaqInput,
  SaveBotProfileInput,
  saveBotLinkSchema,
} from "@/features/bot/schema/bot-schema";
import type { z } from "zod";

/** The parsed (post-transform) link body the API receives. */
type SaveBotLinkPayload = z.output<typeof saveBotLinkSchema>;

/**
 * Talks to wa_api /api/v1/auto-reply-rules. The axios client attaches the bearer token;
 * the API enforces the manage_bot permission on every endpoint.
 *
 * The list endpoint is unpaged — a company's rule set is small and the UI needs it in
 * evaluation order, which the API already applies.
 */
export const AutoReplyRuleService = {
  async getAll(): Promise<AutoReplyRule[]> {
    return executeService(
      { context: "AutoReplyRuleService", method: "getAll" },
      async () => {
        const res = await client.get<ApiSuccessBody<AutoReplyRule[]>>(
          "/api/v1/auto-reply-rules"
        );
        return res.data.data ?? [];
      }
    );
  },

  async getById(id: string): Promise<AutoReplyRule> {
    return executeService(
      { context: "AutoReplyRuleService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<AutoReplyRule>>(
          `/api/v1/auto-reply-rules/${id}`
        );
        return res.data.data;
      }
    );
  },

  async create(input: CreateAutoReplyRuleInput): Promise<AutoReplyRule> {
    return executeService(
      { context: "AutoReplyRuleService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<AutoReplyRule>>(
          "/api/v1/auto-reply-rules",
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateAutoReplyRuleInput): Promise<AutoReplyRule> {
    return executeService(
      { context: "AutoReplyRuleService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<AutoReplyRule>>(
          `/api/v1/auto-reply-rules/${id}`,
          input
        );
        return res.data.data;
      }
    );
  },

  async remove(id: string): Promise<void> {
    return executeService(
      { context: "AutoReplyRuleService", method: "remove" },
      async () => {
        await client.delete(`/api/v1/auto-reply-rules/${id}`);
      }
    );
  },
};

/**
 * Talks to wa_api /api/v1/bot — the AI assistant's configuration and its dry-run preview.
 * Same manage_bot permission as the rules above: both configure the same bot.
 */
export const BotProfileService = {
  async get(): Promise<BotProfile> {
    return executeService({ context: "BotProfileService", method: "get" }, async () => {
      const res = await client.get<ApiSuccessBody<BotProfile>>("/api/v1/bot/profile");
      return res.data.data;
    });
  },

  async save(input: SaveBotProfileInput): Promise<BotProfile> {
    return executeService({ context: "BotProfileService", method: "save" }, async () => {
      // PUT, not POST: there is exactly one profile per company, so the client never has to
      // know whether it already exists.
      const res = await client.put<ApiSuccessBody<BotProfile>>("/api/v1/bot/profile", input);
      return res.data.data;
    });
  },

  async getFaqs(): Promise<BotFaq[]> {
    return executeService({ context: "BotProfileService", method: "getFaqs" }, async () => {
      const res = await client.get<ApiSuccessBody<BotFaq[]>>("/api/v1/bot/faqs");
      return res.data.data ?? [];
    });
  },

  async createFaq(input: SaveBotFaqInput): Promise<BotFaq> {
    return executeService({ context: "BotProfileService", method: "createFaq" }, async () => {
      const res = await client.post<ApiSuccessBody<BotFaq>>("/api/v1/bot/faqs", input, {
        headers: { "X-Idempotency-Key": createIdempotencyKey() },
      });
      return res.data.data;
    });
  },

  async updateFaq(id: string, input: SaveBotFaqInput): Promise<BotFaq> {
    return executeService({ context: "BotProfileService", method: "updateFaq" }, async () => {
      const res = await client.put<ApiSuccessBody<BotFaq>>(`/api/v1/bot/faqs/${id}`, input);
      return res.data.data;
    });
  },

  async removeFaq(id: string): Promise<void> {
    return executeService({ context: "BotProfileService", method: "removeFaq" }, async () => {
      await client.delete(`/api/v1/bot/faqs/${id}`);
    });
  },

  async getLinks(): Promise<BotLink[]> {
    return executeService({ context: "BotProfileService", method: "getLinks" }, async () => {
      const res = await client.get<ApiSuccessBody<BotLink[]>>("/api/v1/bot/links");
      return res.data.data ?? [];
    });
  },

  async createLink(input: SaveBotLinkPayload): Promise<BotLink> {
    return executeService({ context: "BotProfileService", method: "createLink" }, async () => {
      const res = await client.post<ApiSuccessBody<BotLink>>("/api/v1/bot/links", input, {
        headers: { "X-Idempotency-Key": createIdempotencyKey() },
      });
      return res.data.data;
    });
  },

  async updateLink(id: string, input: SaveBotLinkPayload): Promise<BotLink> {
    return executeService({ context: "BotProfileService", method: "updateLink" }, async () => {
      const res = await client.put<ApiSuccessBody<BotLink>>(`/api/v1/bot/links/${id}`, input);
      return res.data.data;
    });
  },

  async removeLink(id: string): Promise<void> {
    return executeService({ context: "BotProfileService", method: "removeLink" }, async () => {
      await client.delete(`/api/v1/bot/links/${id}`);
    });
  },

  async getPrompt(): Promise<BotPrompt> {
    return executeService({ context: "BotProfileService", method: "getPrompt" }, async () => {
      const res = await client.get<ApiSuccessBody<BotPrompt>>("/api/v1/bot/prompt");
      return res.data.data;
    });
  },

  async preview(turns: BotPreviewTurn[]): Promise<BotPreviewResult> {
    return executeService({ context: "BotProfileService", method: "preview" }, async () => {
      const res = await client.post<ApiSuccessBody<BotPreviewResult>>("/api/v1/bot/preview", {
        turns,
      });
      return res.data.data;
    });
  },
};
