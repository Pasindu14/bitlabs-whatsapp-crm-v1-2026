import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { Subscription } from "@/features/subscriptions/types";
import type {
  Invoice,
  AvailablePlan,
  SubscriptionPurchase,
  PayHereCheckoutPayload,
} from "@/features/my-subscription/types";

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

  async getInvoices(): Promise<Invoice[]> {
    return executeService(
      { context: "MySubscriptionService", method: "getInvoices" },
      async () => {
        const res = await client.get<ApiSuccessBody<Invoice[]>>("/api/v1/my-subscription/invoices");
        return res.data.data;
      }
    );
  },

  async getSubscriptionHistory(): Promise<SubscriptionPurchase[]> {
    return executeService(
      { context: "MySubscriptionService", method: "getSubscriptionHistory" },
      async () => {
        const res = await client.get<ApiSuccessBody<SubscriptionPurchase[]>>(
          "/api/v1/my-subscription/subscription-history"
        );
        return res.data.data;
      }
    );
  },

  async getAvailablePlans(): Promise<AvailablePlan[]> {
    return executeService(
      { context: "MySubscriptionService", method: "getAvailablePlans" },
      async () => {
        const res = await client.get<ApiSuccessBody<AvailablePlan[]>>("/api/v1/my-subscription/available-plans");
        return res.data.data;
      }
    );
  },

  async createCheckoutSession(planId: string): Promise<string> {
    return executeService(
      { context: "MySubscriptionService", method: "createCheckoutSession" },
      async () => {
        const res = await client.post<ApiSuccessBody<{ url: string }>>("/api/v1/my-subscription/checkout", { planId });
        return res.data.data.url;
      }
    );
  },

  async createPortalSession(): Promise<string> {
    return executeService(
      { context: "MySubscriptionService", method: "createPortalSession" },
      async () => {
        const res = await client.post<ApiSuccessBody<{ url: string }>>("/api/v1/my-subscription/portal", {});
        return res.data.data.url;
      }
    );
  },

  async createPayHereCheckout(planId: string): Promise<PayHereCheckoutPayload> {
    return executeService(
      { context: "MySubscriptionService", method: "createPayHereCheckout" },
      async () => {
        const res = await client.post<ApiSuccessBody<PayHereCheckoutPayload>>(
          "/api/v1/my-subscription/payhere-checkout",
          { planId }
        );
        return res.data.data;
      }
    );
  },
};
