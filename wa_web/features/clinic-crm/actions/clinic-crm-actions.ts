"use server";

import { createAction } from "@/lib/actions/wrapper";
import { ClinicCrmService } from "@/features/clinic-crm/services/clinic-crm-service";
import {
  saveClinicCrmConnectionSchema,
  saveClinicCrmMappingsSchema,
} from "@/features/clinic-crm/schema/clinic-crm-schema";

// The API gates these on manage_bot, which CompanyAdmin holds implicitly. The role check is
// defense-in-depth on top of that — the same arrangement as the rest of the chatbot settings.
const COMPANY_ADMIN = "CompanyAdmin";

export const getClinicCrmAction = createAction(
  { name: "getClinicCrmAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => ClinicCrmService.get()
);

export const saveClinicCrmAction = createAction(
  { name: "saveClinicCrmAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = saveClinicCrmConnectionSchema.parse(raw);
    return ClinicCrmService.save(input);
  }
);

export const deleteClinicCrmAction = createAction(
  { name: "deleteClinicCrmAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => ClinicCrmService.remove()
);

export const testClinicCrmAction = createAction(
  { name: "testClinicCrmAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => ClinicCrmService.test()
);

export const getClinicCrmTreatmentsAction = createAction(
  { name: "getClinicCrmTreatmentsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => ClinicCrmService.getTreatments()
);

export const saveClinicCrmMappingsAction = createAction(
  { name: "saveClinicCrmMappingsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = saveClinicCrmMappingsSchema.parse(raw);
    return ClinicCrmService.saveMappings(input);
  }
);
