"use server";

import { createAction } from "@/lib/actions/wrapper";
import { FxService } from "@/features/fx/services/fx-service";

// No role restriction: the customer plan picker (CompanyAdmin) and the platform plans table
// (SuperAdmin) both render dirham equivalents, and the rate itself is not tenant data.
export const getUsdToAedRateAction = createAction(
  { name: "getUsdToAedRateAction", requireAuth: true },
  async () => {
    return FxService.getUsdToAed();
  }
);
