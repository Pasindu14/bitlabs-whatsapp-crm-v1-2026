/** A company as returned by wa_api (mirrors CompanyResponse). */
export interface Company {
  id: string;
  name: string;
  slug: string | null;
  email: string | null;
  phone: string | null;
  isActive: boolean;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface CompanyListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
