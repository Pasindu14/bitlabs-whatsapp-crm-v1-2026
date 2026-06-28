import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { PaginatedResponse } from "@/lib/types/actions";
import type {
  Subscription,
  SubscriptionListParams,
  PackagePurchase,
} from "@/features/subscriptions/types";
import type {
  AssignSubscriptionInput,
  ChangePlanInput,
  AddPackageInput,
} from "@/features/subscriptions/schema/subscription-schema";

/**
 * Talks to wa_api /api/v1/subscriptions (SuperAdmin). The axios client attaches the
 * bearer token; the API enforces SuperAdmin-only access on every endpoint.
 */
export const SubscriptionService = {
  async getPaginated(
    params: SubscriptionListParams
  ): Promise<PaginatedResponse<Subscription>> {
    return executeService(
      { context: "SubscriptionService", method: "getPaginated" },
      async () => {
        const res = await client.get<ApiSuccessBody<Subscription[]>>(
          "/api/v1/subscriptions",
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

  async getForCompany(companyId: string): Promise<Subscription> {
    return executeService(
      { context: "SubscriptionService", method: "getForCompany" },
      async () => {
        const res = await client.get<ApiSuccessBody<Subscription>>(
          `/api/v1/subscriptions/company/${companyId}`
        );
        return res.data.data;
      }
    );
  },

  async assign(input: AssignSubscriptionInput): Promise<Subscription> {
    return executeService(
      { context: "SubscriptionService", method: "assign" },
      async () => {
        const res = await client.post<ApiSuccessBody<Subscription>>(
          "/api/v1/subscriptions/assign",
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async changePlan(companyId: string, input: ChangePlanInput): Promise<Subscription> {
    return executeService(
      { context: "SubscriptionService", method: "changePlan" },
      async () => {
        const res = await client.put<ApiSuccessBody<Subscription>>(
          `/api/v1/subscriptions/company/${companyId}/plan`,
          input
        );
        return res.data.data;
      }
    );
  },

  async cancel(companyId: string): Promise<Subscription> {
    return executeService(
      { context: "SubscriptionService", method: "cancel" },
      async () => {
        const res = await client.post<ApiSuccessBody<Subscription>>(
          `/api/v1/subscriptions/company/${companyId}/cancel`
        );
        return res.data.data;
      }
    );
  },

  async addPackage(companyId: string, input: AddPackageInput): Promise<Subscription> {
    return executeService(
      { context: "SubscriptionService", method: "addPackage" },
      async () => {
        const res = await client.post<ApiSuccessBody<Subscription>>(
          `/api/v1/subscriptions/company/${companyId}/packages`,
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async getPackageHistory(companyId: string): Promise<PackagePurchase[]> {
    return executeService(
      { context: "SubscriptionService", method: "getPackageHistory" },
      async () => {
        const res = await client.get<ApiSuccessBody<PackagePurchase[]>>(
          `/api/v1/subscriptions/company/${companyId}/packages`
        );
        return res.data.data ?? [];
      }
    );
  },
};
