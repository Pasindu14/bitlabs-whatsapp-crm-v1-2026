"use server";

import { createAction } from "@/lib/actions/wrapper";
import { AnalyticsService } from "@/features/analytics/services/analytics-service";

const COMPANY_ADMIN = "CompanyAdmin";

export const getMessageMetricsAction = createAction(
  { name: "getMessageMetricsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (from?: string, to?: string) => AnalyticsService.getMessageMetrics(from, to)
);

export const getCampaignPerformanceAction = createAction(
  { name: "getCampaignPerformanceAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (from?: string, to?: string) => AnalyticsService.getCampaignPerformance(from, to)
);

export const getCostAnalyticsAction = createAction(
  { name: "getCostAnalyticsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (from?: string, to?: string) => AnalyticsService.getCostAnalytics(from, to)
);
