import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { Message, MessageListParams } from "@/features/messages/types";
import type { SendMessageInput } from "@/features/messages/schema/message-schema";

export const MessageService = {
  async getPaginated(params: MessageListParams): Promise<PaginatedResponse<Message>> {
    return executeService(
      { context: "MessageService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Message[]>>("/api/v1/messages", {
          params: {
            page: params.page,
            pageSize: params.pageSize,
            search: params.search || undefined,
            sortBy: params.sortBy || undefined,
            sortOrder: params.sortOrder || undefined,
          },
        });
        const items = res.data.data ?? [];
        const p = res.data.pagination ?? { page: params.page, pageSize: params.pageSize, total: items.length, totalPages: 1 };
        return {
          items,
          pagination: {
            page: p.page,
            pageSize: p.pageSize,
            total: p.total,
            totalPages: p.totalPages,
            hasMore: p.page < p.totalPages,
          },
        };
      }
    );
  },

  async send(contactId: string, input: SendMessageInput): Promise<Message> {
    return executeService(
      { context: "MessageService", method: "send" },
      async () => {
        const res = await client.post<ApiSuccessBody<Message>>("/api/v1/messages", {
          contactId,
          wabaConnectionId: input.wabaConnectionId,
          body: input.body,
        }, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },
};
