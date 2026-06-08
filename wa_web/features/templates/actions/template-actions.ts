"use server";

import { createAction } from "@/lib/actions/wrapper";
import { TemplateService } from "@/features/templates/services/template-service";
import type { TemplateListParams, TemplatePayload } from "@/features/templates/types";

// Templates are owned by a company; only its CompanyAdmin manages them. The wrapper enforces
// auth + role BEFORE the handler runs (defense-in-depth on top of the API's
// [Authorize(Roles="CompanyAdmin")] + JWT company scoping). The API re-validates every payload.
const COMPANY_ADMIN = "CompanyAdmin";

export const getTemplatesAction = createAction(
  { name: "getTemplatesAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: TemplateListParams) => TemplateService.getPaginated(params)
);

export const getTemplateByIdAction = createAction(
  { name: "getTemplateByIdAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => TemplateService.getById(id)
);

export const createTemplateAction = createAction(
  { name: "createTemplateAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (input: TemplatePayload) => TemplateService.create(input)
);

export const updateTemplateAction = createAction(
  { name: "updateTemplateAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, input: TemplatePayload) => TemplateService.update(id, input)
);

export const submitTemplateAction = createAction(
  { name: "submitTemplateAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => TemplateService.submit(id)
);

export const refreshTemplateStatusAction = createAction(
  { name: "refreshTemplateStatusAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => TemplateService.refreshStatus(id)
);

export const deleteTemplateAction = createAction(
  { name: "deleteTemplateAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => TemplateService.remove(id)
);
