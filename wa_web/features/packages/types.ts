/** A message-credit package as returned by wa_api (mirrors PackageResponse). */
export interface MessagePackage {
  id: string;
  name: string;
  description: string | null;
  extraMessages: number;
  price: number;
  currency: string;
  isActive: boolean;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface PackageListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
