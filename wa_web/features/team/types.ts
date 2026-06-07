import type { TeamRole } from "@/features/team/schema/team-schema";

/** A team member as returned by wa_api (mirrors UserResponse). Never carries a password. */
export interface TeamMember {
  id: string;
  companyId: string | null;
  companyName: string | null;
  fullName: string;
  email: string;
  role: TeamRole;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface TeamListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
