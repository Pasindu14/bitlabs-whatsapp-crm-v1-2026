"use server";

import client, { type ApiSuccessBody } from "@/lib/api/client";
import { createAction } from "@/lib/actions/wrapper";

export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
  exception: string | null;
}

export const getLogsAction = createAction(
  { name: "getLogsAction", requireAuth: true, requiredRole: "SuperAdmin" },
  async (since: string | null) => {
    const params: Record<string, string | number> = { tail: 100 };
    if (since) params.since = since;

    const res = await client.get<ApiSuccessBody<LogEntry[]>>("/api/v1/admin/logs", { params });
    return res.data.data ?? [];
  }
);
