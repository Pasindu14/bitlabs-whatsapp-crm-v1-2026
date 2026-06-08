import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type {
  ContactList,
  ContactListListParams,
  ImportContactsResult,
} from "@/features/contact-lists/types";
import type {
  CreateContactListInput,
  UpdateContactListInput,
} from "@/features/contact-lists/schema/contact-list-schema";

/**
 * Talks to wa_api /api/v1/contact-lists. The axios client attaches the caller's bearer token;
 * the API enforces CompanyAdmin-only access and scopes every row to the caller's company.
 */
export const ContactListService = {
  async getPaginated(params: ContactListListParams): Promise<PaginatedResponse<ContactList>> {
    return executeService(
      { context: "ContactListService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<ContactList[]>>("/api/v1/contact-lists", {
          params: {
            page: params.page,
            pageSize: params.pageSize,
            search: params.search || undefined,
            sortBy: params.sortBy || undefined,
            sortOrder: params.sortOrder || undefined,
          },
        });

        const items = res.data.data ?? [];
        const p = res.data.pagination ?? {
          page: params.page,
          pageSize: params.pageSize,
          total: items.length,
          totalPages: 1,
        };

        return {
          items,
          pagination: {
            page: p.page,
            pageSize: p.pageSize,
            total: p.total,
            totalPages: p.totalPages,
            hasMore: p.page < p.totalPages,
          },
        };
      }
    );
  },

  async getById(id: string): Promise<ContactList> {
    return executeService(
      { context: "ContactListService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<ContactList>>(`/api/v1/contact-lists/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreateContactListInput): Promise<ContactList> {
    return executeService(
      { context: "ContactListService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<ContactList>>("/api/v1/contact-lists", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateContactListInput): Promise<ContactList> {
    return executeService(
      { context: "ContactListService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<ContactList>>(`/api/v1/contact-lists/${id}`, input);
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<ContactList> {
    return executeService(
      { context: "ContactListService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<ContactList>>(`/api/v1/contact-lists/${id}/activate`);
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<ContactList> {
    return executeService(
      { context: "ContactListService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<ContactList>>(`/api/v1/contact-lists/${id}/deactivate`);
        return res.data.data;
      }
    );
  },

  /** Add one or more existing contacts to a list. Returns the refreshed list. */
  async addContacts(id: string, contactIds: string[]): Promise<ContactList> {
    return executeService(
      { context: "ContactListService", method: "addContacts" },
      async () => {
        const res = await client.post<ApiSuccessBody<ContactList>>(
          `/api/v1/contact-lists/${id}/contacts`,
          { contactIds }
        );
        return res.data.data;
      }
    );
  },

  /** Remove a contact from a list (the contact itself is kept). */
  async removeContact(id: string, contactId: string): Promise<void> {
    return executeService(
      { context: "ContactListService", method: "removeContact" },
      async () => {
        await client.delete(`/api/v1/contact-lists/${id}/contacts/${contactId}`);
      }
    );
  },

  /** Upload an .xlsx (Phone + Name columns) and bulk-add contacts to a list. */
  async importContacts(id: string, formData: FormData): Promise<ImportContactsResult> {
    return executeService(
      { context: "ContactListService", method: "importContacts" },
      async () => {
        const res = await client.post<ApiSuccessBody<ImportContactsResult>>(
          `/api/v1/contact-lists/${id}/import`,
          formData,
          // Drop the client's default JSON content-type so axios sets multipart + boundary.
          { headers: { "Content-Type": undefined } }
        );
        return res.data.data;
      }
    );
  },
};
