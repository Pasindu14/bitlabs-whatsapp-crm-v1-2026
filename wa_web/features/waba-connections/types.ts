import type { WabaConnectionStatus } from "@/features/waba-connections/schema/waba-connection-schema";

/** A WABA connection as returned by wa_api (mirrors WabaConnectionResponse). */
export interface WabaConnection {
  id: string;
  companyId: string;
  companyName: string | null;
  phoneNumberId: string;
  wabaId: string;
  displayPhoneNumber: string;
  status: WabaConnectionStatus;
  hasAccessToken: boolean;
  isActive: boolean;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface WabaConnectionListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
