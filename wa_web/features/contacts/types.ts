/** A contact as returned by wa_api (mirrors ContactResponse). */
export interface Contact {
  id: string;
  phone: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** Filters/pagination the list endpoint accepts. */
export interface ContactListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
  /** When set, only contacts that are members of this contact list are returned. */
  listId?: string;
}
