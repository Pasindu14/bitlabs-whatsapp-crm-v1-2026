import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { MessageMetrics, CampaignPerformance, CostAnalytics } from "@/features/analytics/types";

function buildParams(from?: string, to?: string) {
  const params: Record<string, string> = {};
  if (from) params.from = from;
  if (to) params.to = to;
  return params;
}

export const AnalyticsService = {
  async getMessageMetrics(from?: string, to?: string): Promise<MessageMetrics> {
    return executeService(
      { context: "AnalyticsService", method: "getMessageMetrics" },
      async () => {
        const res = await client.get<ApiSuccessBody<MessageMetrics>>(
          "/api/v1/analytics/messages",
          { params: buildParams(from, to) }
        );
        return res.data.data;
      }
    );
  },

  async getCampaignPerformance(from?: string, to?: string): Promise<CampaignPerformance> {
    return executeService(
      { context: "AnalyticsService", method: "getCampaignPerformance" },
      async () => {
        const res = await client.get<ApiSuccessBody<CampaignPerformance>>(
          "/api/v1/analytics/campaigns",
          { params: buildParams(from, to) }
        );
        return res.data.data;
      }
    );
  },

  async getCostAnalytics(from?: string, to?: string): Promise<CostAnalytics> {
    return executeService(
      { context: "AnalyticsService", method: "getCostAnalytics" },
      async () => {
        const res = await client.get<ApiSuccessBody<CostAnalytics>>(
          "/api/v1/analytics/costs",
          { params: buildParams(from, to) }
        );
        return res.data.data;
      }
    );
  },
};
