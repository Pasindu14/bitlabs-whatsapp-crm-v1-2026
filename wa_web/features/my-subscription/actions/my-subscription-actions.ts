"use server";

import { createAction } from "@/lib/actions/wrapper";
import { MySubscriptionService } from "@/features/my-subscription/services/my-subscription-service";

// Any authenticated company user may read their own subscription. No role restriction —
// the API resolves CompanyId from the JWT and the global query filter enforces isolation.
export const getMySubscriptionAction = createAction(
  { name: "getMySubscriptionAction", requireAuth: true },
  async () => {
    return MySubscriptionService.getCurrent();
  }
);
