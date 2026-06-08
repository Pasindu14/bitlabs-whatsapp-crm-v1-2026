import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { Template, TemplateListParams, TemplatePayload } from "@/features/templates/types";

/**
 * Talks to wa_api /api/v1/templates. The axios client attaches the caller's bearer token; the API
 * enforces CompanyAdmin-only access and scopes every row to the caller's company. (Media uploads
 * go through the /api/templates/media-handle route handler, not this service — they are multipart.)
 */
export const TemplateService = {
  async getPaginated(params: TemplateListParams): Promise<PaginatedResponse<Template>> {
    return executeService(
      { context: "TemplateService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Template[]>>("/api/v1/templates", {
          params: {
            page: params.page,
            pageSize: params.pageSize,
            search: params.search || undefined,
            status: params.status || undefined,
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

  async getById(id: string): Promise<Template> {
    return executeService(
      { context: "TemplateService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<Template>>(`/api/v1/templates/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: TemplatePayload): Promise<Template> {
    return executeService(
      { context: "TemplateService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<Template>>("/api/v1/templates", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: TemplatePayload): Promise<Template> {
    return executeService(
      { context: "TemplateService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<Template>>(`/api/v1/templates/${id}`, input);
        return res.data.data;
      }
    );
  },

  async submit(id: string): Promise<Template> {
    return executeService(
      { context: "TemplateService", method: "submit" },
      async () => {
        const res = await client.post<ApiSuccessBody<Template>>(`/api/v1/templates/${id}/submit`);
        return res.data.data;
      }
    );
  },

  async refreshStatus(id: string): Promise<Template> {
    return executeService(
      { context: "TemplateService", method: "refreshStatus" },
      async () => {
        const res = await client.post<ApiSuccessBody<Template>>(`/api/v1/templates/${id}/refresh-status`);
        return res.data.data;
      }
    );
  },

  async remove(id: string): Promise<Template> {
    return executeService(
      { context: "TemplateService", method: "remove" },
      async () => {
        const res = await client.delete<ApiSuccessBody<Template>>(`/api/v1/templates/${id}`);
        return res.data.data;
      }
    );
  },
};
