/** How a contact's opt-in consent was obtained (mirrors wa_api ConsentSource). */
export type ConsentSource = "None" | "InboundMessage" | "ManualEntry" | "Import";

/** A contact as returned by wa_api (mirrors ContactResponse). */
export interface Contact {
  id: string;
  phone: string;
  name: string;
  isActive: boolean;
  /** Replied STOP/unsubscribe — suppressed from all outbound sends. */
  isOptedOut: boolean;
  optedOutAt: string | null;
  /** Has given explicit opt-in consent to receive business-initiated messages. */
  hasOptedIn: boolean;
  optedInAt: string | null;
  /** How the opt-in was obtained (None until opted in). */
  consentSource: ConsentSource;
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
