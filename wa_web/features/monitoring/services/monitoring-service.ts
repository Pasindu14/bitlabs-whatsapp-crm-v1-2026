import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { MonitoringData } from "@/features/monitoring/types";

export const MonitoringService = {
  async getCompanyHealth(): Promise<MonitoringData> {
    return executeService(
      { context: "MonitoringService", method: "getCompanyHealth" },
      async () => {
        const res = await client.get<ApiSuccessBody<MonitoringData>>(
          "/api/v1/monitoring/company-health"
        );
        return res.data.data;
      }
    );
  },
};
