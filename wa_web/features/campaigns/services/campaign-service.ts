import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type {
  Campaign,
  CampaignStats,
  CampaignRecipient,
  CampaignListParams,
  CampaignAudienceHealth,
} from "@/features/campaigns/types";
import type {
  CreateCampaignInput,
  UpdateCampaignInput,
} from "@/features/campaigns/schema/campaign-schema";

export const CampaignService = {
  async getPaginated(params: CampaignListParams): Promise<PaginatedResponse<Campaign>> {
    return executeService(
      { context: "CampaignService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Campaign[]>>("/api/v1/campaigns", {
          params: {
            page: params.page,
            pageSize: params.pageSize,
            search: params.search || undefined,
            status: params.status || undefined,
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

  async getById(id: string): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "getById" },
      async () => {
        const res = await client.get<ApiSuccessBody<Campaign>>(`/api/v1/campaigns/${id}`);
        return res.data.data;
      }
    );
  },

  async create(input: CreateCampaignInput): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "create" },
      async () => {
        const res = await client.post<ApiSuccessBody<Campaign>>("/api/v1/campaigns", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async update(id: string, input: UpdateCampaignInput): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "update" },
      async () => {
        const res = await client.put<ApiSuccessBody<Campaign>>(`/api/v1/campaigns/${id}`, input);
        return res.data.data;
      }
    );
  },

  async delete(id: string): Promise<void> {
    return executeService(
      { context: "CampaignService", method: "delete" },
      async () => {
        await client.delete(`/api/v1/campaigns/${id}`);
      }
    );
  },

  async launch(id: string): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "launch" },
      async () => {
        const res = await client.post<ApiSuccessBody<Campaign>>(`/api/v1/campaigns/${id}/launch`);
        return res.data.data;
      }
    );
  },

  async pause(id: string): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "pause" },
      async () => {
        const res = await client.post<ApiSuccessBody<Campaign>>(`/api/v1/campaigns/${id}/pause`);
        return res.data.data;
      }
    );
  },

  async resume(id: string): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "resume" },
      async () => {
        const res = await client.post<ApiSuccessBody<Campaign>>(`/api/v1/campaigns/${id}/resume`);
        return res.data.data;
      }
    );
  },

  async cancel(id: string): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "cancel" },
      async () => {
        const res = await client.post<ApiSuccessBody<Campaign>>(`/api/v1/campaigns/${id}/cancel`);
        return res.data.data;
      }
    );
  },

  async getStats(id: string): Promise<CampaignStats> {
    return executeService(
      { context: "CampaignService", method: "getStats" },
      async () => {
        const res = await client.get<ApiSuccessBody<CampaignStats>>(`/api/v1/campaigns/${id}/stats`);
        return res.data.data;
      }
    );
  },

  async getAudienceHealth(id: string): Promise<CampaignAudienceHealth> {
    return executeService(
      { context: "CampaignService", method: "getAudienceHealth" },
      async () => {
        const res = await client.get<ApiSuccessBody<CampaignAudienceHealth>>(
          `/api/v1/campaigns/${id}/audience-health`
        );
        return res.data.data;
      }
    );
  },

  async getRecipients(
    id: string,
    page: number,
    pageSize: number,
    status?: string
  ): Promise<PaginatedResponse<CampaignRecipient>> {
    return executeService(
      { context: "CampaignService", method: "getRecipients" },
      async () => {
        const res = await client.get<ApiSuccessBody<CampaignRecipient[]>>(
          `/api/v1/campaigns/${id}/recipients`,
          { params: { page, pageSize, status: status || undefined } }
        );
        const items = res.data.data ?? [];
        const p = res.data.pagination ?? { page, pageSize, total: items.length, totalPages: 1 };
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

  async duplicate(id: string): Promise<Campaign> {
    return executeService(
      { context: "CampaignService", method: "duplicate" },
      async () => {
        const res = await client.post<ApiSuccessBody<Campaign>>(
          `/api/v1/campaigns/${id}/duplicate`
        );
        return res.data.data;
      }
    );
  },
};
