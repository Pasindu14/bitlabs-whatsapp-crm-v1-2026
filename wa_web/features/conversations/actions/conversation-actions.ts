"use server";

import { createAction } from "@/lib/actions/wrapper";
import { ConversationService } from "@/features/conversations/services/conversation-service";
import {
  sendConversationMessageSchema,
  startConversationSchema,
} from "@/features/conversations/schema/conversation-schema";
import type { ConversationListParams } from "@/features/conversations/types";

const COMPANY_ADMIN = "CompanyAdmin";

export const getConversationsAction = createAction(
  { name: "getConversationsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: ConversationListParams) => {
    return ConversationService.getPaginated(params);
  },
);

export const getConversationMessagesAction = createAction(
  { name: "getConversationMessagesAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (conversationId: string, page = 1, pageSize = 50) => {
    return ConversationService.getMessages(conversationId, page, pageSize);
  },
);

export const sendConversationMessageAction = createAction(
  { name: "sendConversationMessageAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (conversationId: string, raw: unknown) => {
    const input = sendConversationMessageSchema.parse(raw);
    return ConversationService.send(conversationId, input.body);
  },
);

export const markConversationReadAction = createAction(
  { name: "markConversationReadAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (conversationId: string) => {
    await ConversationService.markRead(conversationId);
    return { id: conversationId };
  },
);

export const deleteConversationMessageAction = createAction(
  { name: "deleteConversationMessageAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (conversationId: string, messageId: string) => {
    await ConversationService.deleteMessage(conversationId, messageId);
    return { conversationId, messageId };
  },
);

export const startConversationAction = createAction(
  { name: "startConversationAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = startConversationSchema.parse(raw);
    return ConversationService.start(input);
  },
);
