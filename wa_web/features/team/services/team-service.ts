import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type { TeamMember, TeamListParams } from "@/features/team/types";
import type {
  CreateTeamMemberInput,
  UpdateTeamMemberInput,
  ResetTeamPasswordInput,
} from "@/features/team/schema/team-schema";

/**
 * Talks to wa_api /api/v1/team-users. The axios client attaches the CompanyAdmin bearer
 * token; the API enforces CompanyAdmin-only access and scopes every operation to the
 * caller's own company (the company is taken from the JWT, never the request body).
 */
export const TeamService = {
  async getPaginated(params: TeamListParams): Promise<PaginatedResponse<TeamMember>> {
    return executeService(
      { context: "TeamService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<TeamMember[]>>("/api/v1/team-users", {
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

  async getById(id: string): Promise<TeamMember> {
    return executeService(
      { context: "TeamService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<TeamMember>>(`/api/v1/team-users/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreateTeamMemberInput): Promise<TeamMember> {
    return executeService(
      { context: "TeamService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<TeamMember>>("/api/v1/team-users", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateTeamMemberInput): Promise<TeamMember> {
    return executeService(
      { context: "TeamService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<TeamMember>>(`/api/v1/team-users/${id}`, input);
        return res.data.data;
      }
    );
  },

  async resetPassword(id: string, input: ResetTeamPasswordInput): Promise<TeamMember> {
    return executeService(
      { context: "TeamService", method: "resetPassword" },
      async () => {
        const res = await client.post<ApiSuccessBody<TeamMember>>(
          `/api/v1/team-users/${id}/reset-password`,
          input
        );
        return res.data.data;
      }
    );
  },

  async activate(id: string): Promise<TeamMember> {
    return executeService(
      { context: "TeamService", method: "activate" },
      async () => {
        const res = await client.post<ApiSuccessBody<TeamMember>>(`/api/v1/team-users/${id}/activate`);
        return res.data.data;
      }
    );
  },

  async deactivate(id: string): Promise<TeamMember> {
    return executeService(
      { context: "TeamService", method: "deactivate" },
      async () => {
        const res = await client.post<ApiSuccessBody<TeamMember>>(`/api/v1/team-users/${id}/deactivate`);
        return res.data.data;
      }
    );
  },
};
