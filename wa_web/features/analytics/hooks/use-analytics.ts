"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import {
  getMessageMetricsAction,
  getCampaignPerformanceAction,
  getCostAnalyticsAction,
} from "@/features/analytics/actions/analytics-actions";

export function useMessageMetrics(from?: string, to?: string) {
  return useQuery({
    queryKey: queryKeys.analytics.messages(from, to),
    queryFn: async () => {
      const res = await getMessageMetricsAction(from, to);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useCampaignPerformance(from?: string, to?: string) {
  return useQuery({
    queryKey: queryKeys.analytics.campaigns(from, to),
    queryFn: async () => {
      const res = await getCampaignPerformanceAction(from, to);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useCostAnalytics(from?: string, to?: string) {
  return useQuery({
    queryKey: queryKeys.analytics.costs(from, to),
    queryFn: async () => {
      const res = await getCostAnalyticsAction(from, to);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}
