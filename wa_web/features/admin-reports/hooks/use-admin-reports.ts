"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import {
  getReportsDashboardAction,
  getPackagesReportAction,
  getBalancesReportAction,
  getUsageReportAction,
} from "@/features/admin-reports/actions/admin-reports-actions";

export function useReportsDashboard() {
  return useQuery({
    queryKey: queryKeys.adminReports.dashboard(),
    queryFn: async () => {
      const res = await getReportsDashboardAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 60_000,
  });
}

export function usePackagesReport(from?: string, to?: string) {
  return useQuery({
    queryKey: queryKeys.adminReports.packages(from, to),
    queryFn: async () => {
      const res = await getPackagesReportAction(from, to);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useBalancesReport() {
  return useQuery({
    queryKey: queryKeys.adminReports.balances(),
    queryFn: async () => {
      const res = await getBalancesReportAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 60_000,
  });
}

export function useUsageReport(from?: string, to?: string) {
  return useQuery({
    queryKey: queryKeys.adminReports.usage(from, to),
    queryFn: async () => {
      const res = await getUsageReportAction(from, to);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}
