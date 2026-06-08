"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getMySubscriptionAction } from "@/features/my-subscription/actions/my-subscription-actions";

/** The caller's own active subscription + usage. `hasSubscription` is false when none. */
export function useMySubscription() {
  return useQuery({
    queryKey: queryKeys.mySubscription.current(),
    queryFn: async () => {
      const res = await getMySubscriptionAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 30_000,
  });
}
