import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { User, UserListParams } from "@/features/users/types";
import type {
  CreateUserInput,
  UpdateUserInput,
  ResetPasswordInput,
} from "@/features/users/schema/user-schema";

/**
 * Talks to wa_api /api/v1/users. The axios client attaches the SuperAdmin bearer
 * token; the API enforces SuperAdmin-only access on every endpoint.
 */
export const UserService = {
  async getPaginated(params: UserListParams): Promise<PaginatedResponse<User>> {
    return executeService(
      { context: "UserService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<User[]>>("/api/v1/users", {
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

  async getById(id: string): Promise<User> {
    return executeService(
      { context: "UserService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<User>>(`/api/v1/users/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreateUserInput): Promise<User> {
    return executeService(
      { context: "UserService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<User>>("/api/v1/users", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateUserInput): Promise<User> {
    return executeService(
      { context: "UserService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<User>>(`/api/v1/users/${id}`, input);
        return res.data.data;
      }
    );
  },

  async resetPassword(id: string, input: ResetPasswordInput): Promise<User> {
    return executeService(
      { context: "UserService", method: "resetPassword" },
      async () => {
        const res = await client.post<ApiSuccessBody<User>>(
          `/api/v1/users/${id}/reset-password`,
          input
        );
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<User> {
    return executeService(
      { context: "UserService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<User>>(`/api/v1/users/${id}/activate`);
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<User> {
    return executeService(
      { context: "UserService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<User>>(`/api/v1/users/${id}/deactivate`);
        return res.data.data;
      }
    );
  },
};
