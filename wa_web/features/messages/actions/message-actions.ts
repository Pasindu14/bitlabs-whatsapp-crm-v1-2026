"use server";

import { createAction } from "@/lib/actions/wrapper";
import { MessageService } from "@/features/messages/services/message-service";
import { sendMessageSchema } from "@/features/messages/schema/message-schema";
import type { MessageListParams } from "@/features/messages/types";

const COMPANY_ADMIN = "CompanyAdmin";

export const getMessagesAction = createAction(
  { name: "getMessagesAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: MessageListParams) => {
    return MessageService.getPaginated(params);
  }
);

export const sendMessageAction = createAction(
  { name: "sendMessageAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (contactId: string, raw: unknown) => {
    const input = sendMessageSchema.parse(raw);
    return MessageService.send(contactId, input);
  }
);

export const searchContactsForMessageAction = createAction(
  { name: "searchContactsForMessageAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (search?: string) => {
    const { ContactService } = await import("@/features/contacts/services/contact-service");
    const result = await ContactService.getPaginated({ page: 1, pageSize: 20, search });
    return result.items;
  }
);
