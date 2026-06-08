"use server";

import { createAction } from "@/lib/actions/wrapper";
import { PlanService } from "@/features/plans/services/plan-service";
import {
  createPlanSchema,
  updatePlanSchema,
} from "@/features/plans/schema/plan-schema";
import type { PlanListParams } from "@/features/plans/types";

// Every plan action is SuperAdmin-only — the wrapper enforces auth + role BEFORE
// the handler runs (defense-in-depth on top of the API's [Authorize]).
const SUPERADMIN = "SuperAdmin";

export const getPlansAction = createAction(
  { name: "getPlansAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (params: PlanListParams) => {
    return PlanService.getPaginated(params);
  }
);

export const getPlanByIdAction = createAction(
  { name: "getPlanByIdAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return PlanService.getById(id);
  }
);

export const createPlanAction = createAction(
  { name: "createPlanAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = createPlanSchema.parse(raw);
    return PlanService.create(input);
  }
);

export const updatePlanAction = createAction(
  { name: "updatePlanAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string, raw: unknown) => {
    const input = updatePlanSchema.parse(raw);
    return PlanService.update(id, input);
  }
);

export const activatePlanAction = createAction(
  { name: "activatePlanAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return PlanService.activate(id);
  }
);

export const deactivatePlanAction = createAction(
  { name: "deactivatePlanAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return PlanService.deactivate(id);
  }
);
