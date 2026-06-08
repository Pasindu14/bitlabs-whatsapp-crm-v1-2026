"use client";

import { useSession, signOut } from "next-auth/react";
import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";

/**
 * Watches for session token expiry and signs the user out automatically.
 *
 * Two scenarios are handled:
 *  1. `session.error === "RefreshAccessTokenError"` — the NextAuth jwt callback
 *     tried to refresh the .NET access token but the refresh token was also
 *     invalid/expired. The session is unrecoverable; sign out immediately.
 *
 *  2. Any mutation that receives `{ success: false, code: "AUTH_TOKEN_EXPIRED" }`
 *     from a server action (i.e. the API rejected the bearer token before the
 *     proactive refresh window fired). Detected via the TanStack Query mutation
 *     cache subscription so no per-hook changes are needed.
 */
export function SessionGuard({ children }: { children: React.ReactNode }) {
  const { data: session } = useSession();
  const queryClient = useQueryClient();

  // Scenario 1 — refresh token exhausted
  useEffect(() => {
    if (session?.error === "RefreshAccessTokenError") {
      queryClient.clear();
      signOut({ callbackUrl: "/sign-in" });
    }
  }, [session?.error, queryClient]);

  // Scenario 2 — access token rejected by the API mid-session
  useEffect(() => {
    return queryClient.getMutationCache().subscribe((event) => {
      if (event.type === "updated" && event.mutation.state.status === "error") {
        const error = event.mutation.state.error as { code?: string } | null;
        if (error?.code === "AUTH_TOKEN_EXPIRED") {
          queryClient.clear();
          signOut({ callbackUrl: "/sign-in" });
        }
      }
    });
  }, [queryClient]);

  return <>{children}</>;
}
