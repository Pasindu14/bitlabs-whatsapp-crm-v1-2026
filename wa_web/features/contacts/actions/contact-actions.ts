"use server";

import { createAction } from "@/lib/actions/wrapper";
import { ContactService } from "@/features/contacts/services/contact-service";
import {
  createContactSchema,
  updateContactSchema,
} from "@/features/contacts/schema/contact-schema";
import type { ContactListParams, ImportContactsInput } from "@/features/contacts/types";

// Contacts are owned by a company; only its CompanyAdmin manages them. The wrapper
// enforces auth + role BEFORE the handler runs (defense-in-depth on top of the API's
// [Authorize(Roles="CompanyAdmin")] + JWT company scoping).
const COMPANY_ADMIN = "CompanyAdmin";

export const getContactsAction = createAction(
  { name: "getContactsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: ContactListParams) => {
    return ContactService.getPaginated(params);
  }
);

export const getContactByIdAction = createAction(
  { name: "getContactByIdAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return ContactService.getById(id);
  }
);

export const createContactAction = createAction(
  { name: "createContactAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = createContactSchema.parse(raw);
    return ContactService.create(input);
  }
);

export const importContactsAction = createAction(
  { name: "importContactsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (input: ImportContactsInput) => {
    return ContactService.import(input);
  }
);

export const updateContactAction = createAction(
  { name: "updateContactAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = updateContactSchema.parse(raw);
    return ContactService.update(id, input);
  }
);

export const activateContactAction = createAction(
  { name: "activateContactAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return ContactService.activate(id);
  }
);

export const deactivateContactAction = createAction(
  { name: "deactivateContactAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return ContactService.deactivate(id);
  }
);
