import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { Plan, PlanListParams } from "@/features/plans/types";
import type {
  CreatePlanInput,
  UpdatePlanInput,
} from "@/features/plans/schema/plan-schema";

/**
 * Talks to wa_api /api/v1/plans. The axios client attaches the SuperAdmin bearer token;
 * the API enforces SuperAdmin-only access on every endpoint.
 */
export const PlanService = {
  async getPaginated(params: PlanListParams): Promise<PaginatedResponse<Plan>> {
    return executeService(
      { context: "PlanService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Plan[]>>("/api/v1/plans", {
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

  async getById(id: string): Promise<Plan> {
    return executeService(
      { context: "PlanService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<Plan>>(`/api/v1/plans/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreatePlanInput): Promise<Plan> {
    return executeService(
      { context: "PlanService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<Plan>>("/api/v1/plans", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdatePlanInput): Promise<Plan> {
    return executeService(
      { context: "PlanService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<Plan>>(`/api/v1/plans/${id}`, input);
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<Plan> {
    return executeService(
      { context: "PlanService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<Plan>>(`/api/v1/plans/${id}/activate`);
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<Plan> {
    return executeService(
      { context: "PlanService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<Plan>>(`/api/v1/plans/${id}/deactivate`);
        return res.data.data;
      }
    );
  },
};
