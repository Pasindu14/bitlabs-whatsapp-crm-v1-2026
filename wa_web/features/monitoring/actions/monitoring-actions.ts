"use server";

import { createAction } from "@/lib/actions/wrapper";
import { MonitoringService } from "@/features/monitoring/services/monitoring-service";

export const getCompanyHealthAction = createAction(
  { name: "getCompanyHealthAction", requireAuth: true, requiredRole: "SuperAdmin" },
  async () => MonitoringService.getCompanyHealth()
);
