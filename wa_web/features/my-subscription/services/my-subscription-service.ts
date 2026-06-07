import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { Subscription } from "@/features/subscriptions/types";

/**
 * Talks to wa_api /api/v1/my-subscription. Any authenticated company user may read their
 * OWN subscription; CompanyId comes from the JWT (never the client).
 */
export const MySubscriptionService = {
  async getCurrent(): Promise<Subscription> {
    return executeService(
      { context: "MySubscriptionService", method: "getCurrent" },
      async () => {
        const res = await client.get<ApiSuccessBody<Subscription>>("/api/v1/my-subscription");
        return res.data.data;
      }
    );
  },
};
