"use server";

import { createAction } from "@/lib/actions/wrapper";
import { SubscriptionService } from "@/features/subscriptions/services/subscription-service";
import {
  assignSubscriptionSchema,
  changePlanSchema,
  addPackageSchema,
} from "@/features/subscriptions/schema/subscription-schema";
import type { SubscriptionListParams } from "@/features/subscriptions/types";

// Every subscription-admin action is SuperAdmin-only — the wrapper enforces auth + role
// BEFORE the handler runs (defense-in-depth on top of the API's [Authorize]).
const SUPERADMIN = "SuperAdmin";

export const getSubscriptionsAction = createAction(
  { name: "getSubscriptionsAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (params: SubscriptionListParams) => {
    return SubscriptionService.getPaginated(params);
  }
);

export const assignSubscriptionAction = createAction(
  { name: "assignSubscriptionAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = assignSubscriptionSchema.parse(raw);
    return SubscriptionService.assign(input);
  }
);

export const changePlanAction = createAction(
  { name: "changePlanAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (companyId: string, raw: unknown) => {
    const input = changePlanSchema.parse(raw);
    return SubscriptionService.changePlan(companyId, input);
  }
);

export const cancelSubscriptionAction = createAction(
  { name: "cancelSubscriptionAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (companyId: string) => {
    return SubscriptionService.cancel(companyId);
  }
);

export const addPackageAction = createAction(
  { name: "addPackageAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (companyId: string, raw: unknown) => {
    const input = addPackageSchema.parse(raw);
    return SubscriptionService.addPackage(companyId, input);
  }
);

export const getPackageHistoryAction = createAction(
  { name: "getPackageHistoryAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (companyId: string) => {
    return SubscriptionService.getPackageHistory(companyId);
  }
);

export const getSubscriptionHistoryAction = createAction(
  { name: "getSubscriptionHistoryAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (companyId: string) => {
    return SubscriptionService.getSubscriptionHistory(companyId);
  }
);
