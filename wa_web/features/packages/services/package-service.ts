import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { MessagePackage, PackageListParams } from "@/features/packages/types";
import type {
  CreatePackageInput,
  UpdatePackageInput,
} from "@/features/packages/schema/package-schema";

/**
 * Talks to wa_api /api/v1/packages. The axios client attaches the SuperAdmin bearer token;
 * the API enforces SuperAdmin-only access on every endpoint.
 */
export const PackageService = {
  async getPaginated(params: PackageListParams): Promise<PaginatedResponse<MessagePackage>> {
    return executeService(
      { context: "PackageService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<MessagePackage[]>>("/api/v1/packages", {
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

  async getById(id: string): Promise<MessagePackage> {
    return executeService(
      { context: "PackageService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<MessagePackage>>(`/api/v1/packages/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreatePackageInput): Promise<MessagePackage> {
    return executeService(
      { context: "PackageService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<MessagePackage>>("/api/v1/packages", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdatePackageInput): Promise<MessagePackage> {
    return executeService(
      { context: "PackageService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<MessagePackage>>(`/api/v1/packages/${id}`, input);
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<MessagePackage> {
    return executeService(
      { context: "PackageService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<MessagePackage>>(`/api/v1/packages/${id}/activate`);
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<MessagePackage> {
    return executeService(
      { context: "PackageService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<MessagePackage>>(`/api/v1/packages/${id}/deactivate`);
        return res.data.data;
      }
    );
  },
};
