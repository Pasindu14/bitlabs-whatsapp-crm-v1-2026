"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getCompanyHealthAction } from "@/features/monitoring/actions/monitoring-actions";

export function useCompanyHealth() {
  return useQuery({
    queryKey: queryKeys.monitoring.companyHealth(),
    queryFn: async () => {
      const res = await getCompanyHealthAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 60_000,
  });
}
