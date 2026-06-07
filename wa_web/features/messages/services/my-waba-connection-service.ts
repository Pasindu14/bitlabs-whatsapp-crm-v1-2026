import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";

export interface ActiveWabaConnection {
  id: string;
  displayPhoneNumber: string;
  wabaId: string;
  status: string;
}

export const MyWabaConnectionService = {
  async getActive(): Promise<ActiveWabaConnection[]> {
    return executeService(
      { context: "MyWabaConnectionService", method: "getActive" },
      async () => {
        const res = await client.get<ApiSuccessBody<ActiveWabaConnection[]>>("/api/v1/my-waba-connections");
        return res.data.data ?? [];
      }
    );
  },
};
