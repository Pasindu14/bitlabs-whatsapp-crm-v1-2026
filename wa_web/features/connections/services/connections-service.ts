import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { MyWabaConnection } from "@/features/connections/types";

export const ConnectionsService = {
  async getAll(): Promise<MyWabaConnection[]> {
    return executeService(
      { context: "ConnectionsService", method: "getAll" },
      async () => {
        const res = await client.get<ApiSuccessBody<MyWabaConnection[]>>(
          "/api/v1/my-waba-connections/all"
        );
        return res.data.data ?? [];
      }
    );
  },
};
