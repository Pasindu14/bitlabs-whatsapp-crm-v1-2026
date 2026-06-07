"use server";

import { createAction } from "@/lib/actions/wrapper";
import { MyWabaConnectionService } from "@/features/messages/services/my-waba-connection-service";

const COMPANY_ADMIN = "CompanyAdmin";

export const getActiveWabaConnectionsAction = createAction(
  { name: "getActiveWabaConnectionsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => {
    return MyWabaConnectionService.getActive();
  }
);
