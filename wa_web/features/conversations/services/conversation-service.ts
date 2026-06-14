import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type {
  Conversation,
  ConversationListParams,
  ConversationMessage,
} from "@/features/conversations/types";

function toPaginated<T>(
  data: T[],
  pagination: ApiSuccessBody<T[]>["pagination"],
  params: { page: number; pageSize: number },
): PaginatedResponse<T> {
  const p = pagination ?? { page: params.page, pageSize: params.pageSize, total: data.length, totalPages: 1 };
  return {
    items: data,
    pagination: {
      page: p.page,
      pageSize: p.pageSize,
      total: p.total,
      totalPages: p.totalPages,
      hasMore: p.page < p.totalPages,
    },
  };
}

export const ConversationService = {
  async getPaginated(params: ConversationListParams): Promise<PaginatedResponse<Conversation>> {
    return executeService(
      { context: "ConversationService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Conversation[]>>("/api/v1/conversations", {
          params: {
            page: params.page,
            pageSize: params.pageSize,
            search: params.search || undefined,
          },
        });
        return toPaginated(res.data.data ?? [], res.data.pagination, params);
      },
    );
  },

  async getMessages(
    conversationId: string,
    page: number,
    pageSize: number,
  ): Promise<PaginatedResponse<ConversationMessage>> {
    return executeService(
      { context: "ConversationService", method: "getMessages" },
      async () => {
        const res = await client.get<ApiSuccessBody<ConversationMessage[]>>(
          `/api/v1/conversations/${conversationId}/messages`,
          { params: { page, pageSize } },
        );
        return toPaginated(res.data.data ?? [], res.data.pagination, { page, pageSize });
      },
    );
  },

  async send(conversationId: string, body: string): Promise<ConversationMessage> {
    return executeService(
      { context: "ConversationService", method: "send" },
      async () => {
        const res = await client.post<ApiSuccessBody<ConversationMessage>>(
          `/api/v1/conversations/${conversationId}/messages`,
          { body },
          { headers: { "Idempotency-Key": createIdempotencyKey() } },
        );
        return res.data.data;
      },
    );
  },

  async start(input: { contactId: string; wabaConnectionId: string; body: string }): Promise<Conversation> {
    return executeService(
      { context: "ConversationService", method: "start" },
      async () => {
        const res = await client.post<ApiSuccessBody<Conversation>>("/api/v1/conversations", {
          contactId: input.contactId,
          wabaConnectionId: input.wabaConnectionId,
          body: input.body,
        });
        return res.data.data;
      },
    );
  },

  async markRead(conversationId: string): Promise<void> {
    return executeService(
      { context: "ConversationService", method: "markRead" },
      async () => {
        await client.post<ApiSuccessBody<unknown>>(`/api/v1/conversations/${conversationId}/read`, {});
      },
    );
  },
};
