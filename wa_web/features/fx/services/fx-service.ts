import client, { type ApiSuccessBody } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type { FxRate } from "@/features/fx/types";

/** Talks to wa_api /api/v1/fx. Display-only rates — nothing here influences what is charged. */
export const FxService = {
  async getUsdToAed(): Promise<FxRate> {
    return executeService(
      { context: "FxService", method: "getUsdToAed" },
      async () => {
        const res = await client.get<ApiSuccessBody<FxRate>>("/api/v1/fx/usd-aed");
        return res.data.data;
      }
    );
  },
};
