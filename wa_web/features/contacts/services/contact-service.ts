import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type {
  Contact,
  ContactListParams,
  ImportContactsInput,
  ImportContactsResult,
} from "@/features/contacts/types";
import type {
  CreateContactInput,
  UpdateContactInput,
} from "@/features/contacts/schema/contact-schema";

/**
 * Talks to wa_api /api/v1/contacts. The axios client attaches the caller's bearer token;
 * the API enforces CompanyAdmin-only access and scopes every row to the caller's company.
 */
export const ContactService = {
  async getPaginated(params: ContactListParams): Promise<PaginatedResponse<Contact>> {
    return executeService(
      { context: "ContactService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Contact[]>>("/api/v1/contacts", {
          params: {
            page: params.page,
            pageSize: params.pageSize,
            search: params.search || undefined,
            sortBy: params.sortBy || undefined,
            sortOrder: params.sortOrder || undefined,
            listId: params.listId || undefined,
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

  async getById(id: string): Promise<Contact> {
    return executeService(
      { context: "ContactService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<Contact>>(`/api/v1/contacts/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreateContactInput): Promise<Contact> {
    return executeService(
      { context: "ContactService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<Contact>>("/api/v1/contacts", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async import(input: ImportContactsInput): Promise<ImportContactsResult> {
    return executeService(
      { context: "ContactService", method: "import" },
      async () => {
        const res = await client.post<ApiSuccessBody<ImportContactsResult>>(
          "/api/v1/contacts/import",
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateContactInput): Promise<Contact> {
    return executeService(
      { context: "ContactService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<Contact>>(`/api/v1/contacts/${id}`, input);
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<Contact> {
    return executeService(
      { context: "ContactService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<Contact>>(`/api/v1/contacts/${id}/activate`);
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<Contact> {
    return executeService(
      { context: "ContactService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<Contact>>(`/api/v1/contacts/${id}/deactivate`);
        return res.data.data;
      }
    );
  },
};
