"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getUsdToAedRateAction } from "@/features/fx/actions/fx-actions";

/**
 * USD→AED display rate. The long staleTime mirrors the API's 12h server-side cache — the dirham is
 * pegged to the dollar, so refetching per mount would be pure noise.
 *
 * Callers must treat a failure as "show USD only", never as a page error: `formatAedApprox` already
 * returns null for an undefined rate, so the standard usage degrades on its own.
 */
export function useUsdToAedRate() {
  return useQuery({
    queryKey: queryKeys.fx.usdToAed(),
    queryFn: async () => {
      const res = await getUsdToAedRateAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 12 * 60 * 60_000,
    retry: 1,
  });
}
