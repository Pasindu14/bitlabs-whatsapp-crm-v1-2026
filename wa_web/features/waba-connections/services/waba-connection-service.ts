import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type {
  WabaConnection,
  WabaConnectionListParams,
} from "@/features/waba-connections/types";
import type {
  CreateWabaConnectionInput,
  UpdateWabaConnectionInput,
} from "@/features/waba-connections/schema/waba-connection-schema";

/**
 * Talks to wa_api /api/v1/waba-connections. The axios client attaches the SuperAdmin
 * bearer token; the API enforces SuperAdmin-only access on every endpoint.
 */
export const WabaConnectionService = {
  async getPaginated(
    params: WabaConnectionListParams
  ): Promise<PaginatedResponse<WabaConnection>> {
    return executeService(
      { context: "WabaConnectionService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<WabaConnection[]>>(
          "/api/v1/waba-connections",
          {
            params: {
              page: params.page,
              pageSize: params.pageSize,
              search: params.search || undefined,
              sortBy: params.sortBy || undefined,
              sortOrder: params.sortOrder || undefined,
            },
          }
        );

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

  async getById(id: string): Promise<WabaConnection> {
    return executeService(
      { context: "WabaConnectionService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<WabaConnection>>(
          `/api/v1/waba-connections/${id}`
        );
        return res.data.data;
      }
    );
  },

  async create(input: CreateWabaConnectionInput): Promise<WabaConnection> {
    return executeService(
      { context: "WabaConnectionService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<WabaConnection>>(
          "/api/v1/waba-connections",
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateWabaConnectionInput): Promise<WabaConnection> {
    return executeService(
      { context: "WabaConnectionService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<WabaConnection>>(
          `/api/v1/waba-connections/${id}`,
          input
        );
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<WabaConnection> {
    return executeService(
      { context: "WabaConnectionService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<WabaConnection>>(
          `/api/v1/waba-connections/${id}/activate`
        );
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<WabaConnection> {
    return executeService(
      { context: "WabaConnectionService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<WabaConnection>>(
          `/api/v1/waba-connections/${id}/deactivate`
        );
        return res.data.data;
      }
    );
  },
};
