"use server";

import { createAction } from "@/lib/actions/wrapper";
import { ContactListService } from "@/features/contact-lists/services/contact-list-service";
import {
  createContactListSchema,
  updateContactListSchema,
} from "@/features/contact-lists/schema/contact-list-schema";
import type { ContactListListParams } from "@/features/contact-lists/types";

const COMPANY_ADMIN = "CompanyAdmin";

export const getContactListsAction = createAction(
  { name: "getContactListsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: ContactListListParams) => {
    return ContactListService.getPaginated(params);
  }
);

export const getContactListByIdAction = createAction(
  { name: "getContactListByIdAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return ContactListService.getById(id);
  }
);

export const createContactListAction = createAction(
  { name: "createContactListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => {
    const input = createContactListSchema.parse(raw);
    return ContactListService.create(input);
  }
);

export const updateContactListAction = createAction(
  { name: "updateContactListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => {
    const input = updateContactListSchema.parse(raw);
    return ContactListService.update(id, input);
  }
);

export const activateContactListAction = createAction(
  { name: "activateContactListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return ContactListService.activate(id);
  }
);

export const deactivateContactListAction = createAction(
  { name: "deactivateContactListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => {
    return ContactListService.deactivate(id);
  }
);

export const addContactsToListAction = createAction(
  { name: "addContactsToListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, contactIds: string[]) => {
    return ContactListService.addContacts(id, contactIds);
  }
);

export const removeContactFromListAction = createAction(
  { name: "removeContactFromListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, contactId: string) => {
    return ContactListService.removeContact(id, contactId);
  }
);

export const importContactsToListAction = createAction(
  { name: "importContactsToListAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, formData: FormData) => {
    return ContactListService.importContacts(id, formData);
  }
);
