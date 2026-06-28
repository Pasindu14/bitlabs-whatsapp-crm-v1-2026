"use server";

import { createAction } from "@/lib/actions/wrapper";
import { PackageService } from "@/features/packages/services/package-service";
import {
  createPackageSchema,
  updatePackageSchema,
} from "@/features/packages/schema/package-schema";
import type { PackageListParams } from "@/features/packages/types";

// Every package action is SuperAdmin-only — the wrapper enforces auth + role BEFORE
// the handler runs (defense-in-depth on top of the API's [Authorize]).
const SUPERADMIN = "SuperAdmin";

export const getPackagesAction = createAction(
  { name: "getPackagesAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (params: PackageListParams) => {
    return PackageService.getPaginated(params);
  }
);

export const getPackageByIdAction = createAction(
  { name: "getPackageByIdAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return PackageService.getById(id);
  }
);

export const createPackageAction = createAction(
  { name: "createPackageAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = createPackageSchema.parse(raw);
    return PackageService.create(input);
  }
);

export const updatePackageAction = createAction(
  { name: "updatePackageAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string, raw: unknown) => {
    const input = updatePackageSchema.parse(raw);
    return PackageService.update(id, input);
  }
);

export const activatePackageAction = createAction(
  { name: "activatePackageAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return PackageService.activate(id);
  }
);

export const deactivatePackageAction = createAction(
  { name: "deactivatePackageAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return PackageService.deactivate(id);
  }
);
