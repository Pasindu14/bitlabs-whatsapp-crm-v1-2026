import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { Company, CompanyListParams } from "@/features/companies/types";
import type { CreateCompanyInput } from "@/features/companies/schema/company-schema";

/**
 * Talks to wa_api /api/v1/companies. The axios client attaches the SuperAdmin
 * bearer token; the API enforces SuperAdmin-only access on every endpoint.
 */
export const CompanyService = {
  async getPaginated(params: CompanyListParams): Promise<PaginatedResponse<Company>> {
    return executeService(
      { context: "CompanyService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Company[]>>("/api/v1/companies", {
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

  async create(input: CreateCompanyInput): Promise<Company> {
    return executeService(
      { context: "CompanyService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<Company>>("/api/v1/companies", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async remove(id: string): Promise<void> {
    return executeService(
      { context: "CompanyService", method: "remove" },
      async () => {
        await client.delete(`/api/v1/companies/${id}`);
      }
    );
  },
};
