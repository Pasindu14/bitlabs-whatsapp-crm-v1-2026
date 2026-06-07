"use server";

import { createAction } from "@/lib/actions/wrapper";
import { CompanyService } from "@/features/companies/services/company-service";
import { createCompanySchema } from "@/features/companies/schema/company-schema";
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

export const createCompanyAction = createAction(
  { name: "createCompanyAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (raw: unknown) => {
    const input = createCompanySchema.parse(raw);
    return CompanyService.create(input);
  }
);

export const deleteCompanyAction = createAction(
  { name: "deleteCompanyAction", requireAuth: true, requiredRole: SUPERADMIN },
  async (id: string) => {
    await CompanyService.remove(id);
    return { id };
  }
);
