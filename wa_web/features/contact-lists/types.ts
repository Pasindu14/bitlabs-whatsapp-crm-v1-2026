/** A contact list as returned by wa_api (mirrors ContactListResponse). */
export interface ContactList {
  id: string;
  name: string;
  description: string | null;
  contactCount: number;
  isActive: boolean;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface ContactListListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}

/** One row that could not be imported (mirrors wa_api ImportSkippedRow). */
export interface ImportSkippedRow {
  row: number;
  phone: string | null;
  reason: string;
}

/** Outcome of an Excel import (mirrors wa_api ImportContactsResult). */
export interface ImportContactsResult {
  totalRows: number;
  imported: number;
  updated: number;
  addedToList: number;
  skipped: number;
  skippedRows: ImportSkippedRow[];
}
