"use server";

import { createAction } from "@/lib/actions/wrapper";
import { CompanyService } from "@/features/companies/services/company-service";
import {
  createCompanySchema,
  updateCompanySchema,
} from "@/features/companies/schema/company-schema";
import type { CompanyListParams } from "@/features/companies/types";

// Every company action is SuperAdmin-only — the wrapper enforces auth + role
// BEFORE the handler runs (defense-in-depth on top of the API's [Authorize]).
const SUPERADMIN = "SuperAdmin";

export const getCompaniesAction = createAction(
  { name: "getCompaniesAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (params: CompanyListParams) => {
    return CompanyService.getPaginated(params);
  }
);

export const getCompanyByIdAction = createAction(
  { name: "getCompanyByIdAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return CompanyService.getById(id);
  }
);

export const createCompanyAction = createAction(
  { name: "createCompanyAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = createCompanySchema.parse(raw);
    return CompanyService.create(input);
  }
);

export const updateCompanyAction = createAction(
  { name: "updateCompanyAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string, raw: unknown) => {
    const input = updateCompanySchema.parse(raw);
    return CompanyService.update(id, input);
  }
);

export const activateCompanyAction = createAction(
  { name: "activateCompanyAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return CompanyService.activate(id);
  }
);

export const deactivateCompanyAction = createAction(
  { name: "deactivateCompanyAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    return CompanyService.deactivate(id);
  }
);
