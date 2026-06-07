/**
 * Permission catalog — mirrors wa_api Permission.cs. KEYS ARE A CONTRACT:
 * keep them identical to the backend and never rename/reuse a shipped key.
 * To add a capability, add an entry here and in Permission.cs (no migration needed).
 *
 * Permissions are meaningful only for Agents. SuperAdmin/CompanyAdmin are all-access
 * by role and carry an empty permission list.
 */
export const PERMISSIONS = [
  { key: "live_message", label: "Live Message" },
  { key: "token_purchase", label: "Token Purchase" },
  { key: "manage_template", label: "Manage Template" },
  { key: "schedule_campaign", label: "Schedule Campaign" },
  { key: "contact_list", label: "Contact List" },
  { key: "manage_user", label: "Manage User" },
  { key: "analytics", label: "Analytics" },
] as const;

export const PERMISSION_KEYS = PERMISSIONS.map((p) => p.key) as [
  PermissionKey,
  ...PermissionKey[],
];

export type PermissionKey = (typeof PERMISSIONS)[number]["key"];

const LABEL_BY_KEY: Record<string, string> = Object.fromEntries(
  PERMISSIONS.map((p) => [p.key, p.label])
);

/** Friendly label for a permission key (falls back to the raw key if unknown). */
export const permissionLabel = (key: string): string => LABEL_BY_KEY[key] ?? key;
