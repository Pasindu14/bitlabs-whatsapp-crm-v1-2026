"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getConnectionsAction } from "@/features/connections/actions/connections-actions";

export function useConnections() {
  return useQuery({
    queryKey: queryKeys.connections.list(),
    queryFn: async () => {
      const res = await getConnectionsAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}
