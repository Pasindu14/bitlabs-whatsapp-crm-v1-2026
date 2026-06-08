export interface MessageListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}

export type MessageDirection = "Outbound";
export type MessageStatus = "Sent" | "Failed";

export interface Message {
  id: string;
  contactId: string;
  contactName: string;
  contactPhone: string;
  wabaConnectionId: string;
  displayPhoneNumber: string;
  body: string;
  direction: MessageDirection;
  status: MessageStatus;
  externalMessageId: string | null;
  errorMessage: string | null;
  createdAt: string;
  // Index signature required by the DataTable ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}
