"use client";

import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { queryKeys } from "@/lib/hooks/query-keys";
import { getBotAccessAction } from "@/features/bot/actions/bot-access-actions";
import { NO_BOT_ACCESS, type BotAccess } from "@/features/bot/services/bot-access-service";

/**
 * Whether this company's plan includes the chatbot and the calendar.
 *
 * Fails closed: while loading, on error, and for SuperAdmin (who has no company) everything reads as
 * "no access", so chatbot controls never flash up for a company that has not bought them.
 */
export function useBotAccess(): BotAccess & { isLoading: boolean } {
  const { data: session } = useSession();
  const role = session?.user?.role;
  const isTenantUser = role === "CompanyAdmin" || role === "Agent";

  const query = useQuery({
    queryKey: queryKeys.botAccess.all,
    enabled: isTenantUser,
    staleTime: 60_000,
    queryFn: async () => {
      const res = await getBotAccessAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });

  return { ...(query.data ?? NO_BOT_ACCESS), isLoading: isTenantUser && query.isLoading };
}
