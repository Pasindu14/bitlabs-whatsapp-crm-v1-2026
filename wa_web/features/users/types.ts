import type { UserRole } from "@/features/users/schema/user-schema";

/** A tenant user as returned by wa_api (mirrors UserResponse). Never carries a password. */
export interface User {
  id: string;
  companyId: string | null;
  companyName: string | null;
  fullName: string;
  email: string;
  role: UserRole;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface UserListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
