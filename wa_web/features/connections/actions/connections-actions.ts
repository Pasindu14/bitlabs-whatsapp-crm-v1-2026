"use server";

import { createAction } from "@/lib/actions/wrapper";
import { ConnectionsService } from "@/features/connections/services/connections-service";

export const getConnectionsAction = createAction(
  { name: "getConnectionsAction", requireAuth: true, requiredRole: "CompanyAdmin" },
  async () => ConnectionsService.getAll()
);
