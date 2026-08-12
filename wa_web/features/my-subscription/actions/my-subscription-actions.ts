"use server";

import { createAction } from "@/lib/actions/wrapper";
import { MySubscriptionService } from "@/features/my-subscription/services/my-subscription-service";

const COMPANY_ADMIN = "CompanyAdmin";

// Any authenticated company user may read their own subscription. No role restriction —
// the API resolves CompanyId from the JWT and the global query filter enforces isolation.
export const getMySubscriptionAction = createAction(
  { name: "getMySubscriptionAction", requireAuth: true },
  async () => {
    return MySubscriptionService.getCurrent();
  }
);

export const getInvoicesAction = createAction(
  { name: "getInvoicesAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => {
    return MySubscriptionService.getInvoices();
  }
);

export const getSubscriptionHistoryAction = createAction(
  { name: "getSubscriptionHistoryAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => {
    return MySubscriptionService.getSubscriptionHistory();
  }
);

export const getAvailablePlansAction = createAction(
  { name: "getAvailablePlansAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => {
    return MySubscriptionService.getAvailablePlans();
  }
);

export const createCheckoutSessionAction = createAction(
  { name: "createCheckoutSessionAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (planId: string) => {
    return MySubscriptionService.createCheckoutSession(planId);
  }
);

export const createPortalSessionAction = createAction(
  { name: "createPortalSessionAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => {
    return MySubscriptionService.createPortalSession();
  }
);

export const createPayHereCheckoutAction = createAction(
  { name: "createPayHereCheckoutAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (planId: string) => {
    return MySubscriptionService.createPayHereCheckout(planId);
  }
);
