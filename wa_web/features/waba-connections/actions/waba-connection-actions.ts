"use server";

import { createAction } from "@/lib/actions/wrapper";
import { WabaConnectionService } from "@/features/waba-connections/services/waba-connection-service";
import {
  createWabaConnectionSchema,
  updateWabaConnectionSchema,
} from "@/features/waba-connections/schema/waba-connection-schema";
import type { WabaConnectionListParams } from "@/features/waba-connections/types";

// Every WABA action is SuperAdmin-only — the wrapper enforces auth + role
// BEFORE the handler runs (defense-in-depth on top of the API's [Authorize]).
const SUPERADMIN = "SuperAdmin";

export const getWabaConnectionsAction = createAction(
  { name: "getWabaConnectionsAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (params: WabaConnectionListParams) => {
    return WabaConnectionService.getPaginated(params);
  }
);

export const getWabaConnectionByIdAction = createAction(
  { name: "getWabaConnectionByIdAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return WabaConnectionService.getById(id);
  }
);

export const createWabaConnectionAction = createAction(
  { name: "createWabaConnectionAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = createWabaConnectionSchema.parse(raw);
    return WabaConnectionService.create(input);
  }
);

export const updateWabaConnectionAction = createAction(
  { name: "updateWabaConnectionAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string, raw: unknown) => {
    const input = updateWabaConnectionSchema.parse(raw);
    return WabaConnectionService.update(id, input);
  }
);

export const activateWabaConnectionAction = createAction(
  { name: "activateWabaConnectionAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return WabaConnectionService.activate(id);
  }
);

export const deactivateWabaConnectionAction = createAction(
  { name: "deactivateWabaConnectionAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return WabaConnectionService.deactivate(id);
  }
);
