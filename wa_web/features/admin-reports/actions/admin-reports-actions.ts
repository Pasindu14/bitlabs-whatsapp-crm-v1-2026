"use server";

import { createAction } from "@/lib/actions/wrapper";
import { AdminReportsService } from "@/features/admin-reports/services/admin-reports-service";

const SUPER_ADMIN = "SuperAdmin";

export const getReportsDashboardAction = createAction(
  { name: "getReportsDashboardAction", requireAuth: true, requiredRole: SUPER_ADMIN },
  async () => AdminReportsService.getDashboard()
);

export const getPackagesReportAction = createAction(
  { name: "getPackagesReportAction", requireAuth: true, requiredRole: SUPER_ADMIN },
  async (from?: string, to?: string) => AdminReportsService.getPackages(from, to)
);

export const getBalancesReportAction = createAction(
  { name: "getBalancesReportAction", requireAuth: true, requiredRole: SUPER_ADMIN },
  async () => AdminReportsService.getBalances()
);

export const getUsageReportAction = createAction(
  { name: "getUsageReportAction", requireAuth: true, requiredRole: SUPER_ADMIN },
  async (from?: string, to?: string) => AdminReportsService.getUsage(from, to)
);
