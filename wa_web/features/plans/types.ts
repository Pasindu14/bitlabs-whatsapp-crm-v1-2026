/** A plan tier as returned by wa_api (mirrors PlanResponse). */
export interface Plan {
  id: string;
  name: string;
  monthlyMessageQuota: number;
  price: number;
  currency: string;
  featureFlags: string[];
  isOnline: boolean;
  isActive: boolean;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | string[] | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface PlanListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
