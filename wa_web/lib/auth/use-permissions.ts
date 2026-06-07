"use client";

import { useSession } from "next-auth/react";

/**
 * UI-gating helper. `can(key)` is true when the user holds the permission OR is
 * all-access by role (SuperAdmin / CompanyAdmin). Mirrors the backend model where
 * fine-grained grants apply only to Agents.
 *
 * NOTE: this gates the UI only — real enforcement lives in wa_api (Phase 2). Never
 * rely on this for security; it can be bypassed client-side.
 */
export function usePermissions() {
  const { data } = useSession();
  const role = data?.user?.role;
  const permissions = data?.user?.permissions ?? [];

  const can = (key: string) =>
    role === "SuperAdmin" || role === "CompanyAdmin" || permissions.includes(key);

  return { role, permissions, can };
}
