import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type {
  PackagesReport,
  BalancesReport,
  DashboardReport,
  UsageReport,
} from "@/features/admin-reports/types";

function buildParams(from?: string, to?: string) {
  const params: Record<string, string> = {};
  if (from) params.from = from;
  if (to) params.to = to;
  return params;
}

export const AdminReportsService = {
  async getDashboard(): Promise<DashboardReport> {
    return executeService(
      { context: "AdminReportsService", method: "getDashboard" },
      async () => {
        const res = await client.get<ApiSuccessBody<DashboardReport>>(
          "/api/v1/admin-reports/dashboard"
        );
        return res.data.data;
      }
    );
  },

  async getPackages(from?: string, to?: string): Promise<PackagesReport> {
    return executeService(
      { context: "AdminReportsService", method: "getPackages" },
      async () => {
        const res = await client.get<ApiSuccessBody<PackagesReport>>(
          "/api/v1/admin-reports/packages",
          { params: buildParams(from, to) }
        );
        return res.data.data;
      }
    );
  },

  async getBalances(): Promise<BalancesReport> {
    return executeService(
      { context: "AdminReportsService", method: "getBalances" },
      async () => {
        const res = await client.get<ApiSuccessBody<BalancesReport>>(
          "/api/v1/admin-reports/balances"
        );
        return res.data.data;
      }
    );
  },

  async getUsage(from?: string, to?: string): Promise<UsageReport> {
    return executeService(
      { context: "AdminReportsService", method: "getUsage" },
      async () => {
        const res = await client.get<ApiSuccessBody<UsageReport>>(
          "/api/v1/admin-reports/usage",
          { params: buildParams(from, to) }
        );
        return res.data.data;
      }
    );
  },
};
