import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { NotificationListResponse, NotificationListParams } from "@/features/notifications/types";

export const NotificationService = {
  async getList(params: NotificationListParams): Promise<NotificationListResponse> {
    return executeService(
      { context: "NotificationService", method: "getList" },
      async () => {
        const res = await client.get<ApiSuccessBody<NotificationListResponse>>(
          "/api/v1/notifications",
          { params: { page: params.page, pageSize: params.pageSize } }
        );
        return res.data.data;
      }
    );
  },

  async markRead(id: string): Promise<void> {
    return executeService(
      { context: "NotificationService", method: "markRead" },
      async () => {
        await client.patch(`/api/v1/notifications/${id}/read`);
      }
    );
  },

  async markAllRead(): Promise<void> {
    return executeService(
      { context: "NotificationService", method: "markAllRead" },
      async () => {
        await client.patch("/api/v1/notifications/read-all");
      }
    );
  },
};
