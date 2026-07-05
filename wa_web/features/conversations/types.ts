export type ConversationStatus = "Open" | "Closed";
export type MessageDirection = "Outbound" | "Inbound";
// "Accepted" = Meta queued the message (wamid returned) but its 'sent' status webhook hasn't arrived yet.
export type MessageStatus = "Accepted" | "Sent" | "Failed" | "Delivered" | "Read";

/** Inbox list / thread-header row. All timestamps are UTC ISO strings (rendered in local time). */
export interface Conversation {
  id: string;
  contactId: string;
  contactName: string;
  contactPhone: string;
  wabaConnectionId: string;
  displayPhoneNumber: string;
  status: ConversationStatus;
  lastMessageAt: string;
  lastMessageBody: string | null;
  lastMessageDirection: MessageDirection;
  unreadCount: number;
  windowExpiresAt: string | null;
  createdAt: string;
}

/** A single message inside a thread. */
export interface ConversationMessage {
  id: string;
  conversationId: string | null;
  body: string;
  direction: MessageDirection;
  status: MessageStatus;
  externalMessageId: string | null;
  errorMessage: string | null;
  createdAt: string;
  statusAt: string | null;
}

export interface ConversationListParams {
  page: number;
  pageSize: number;
  search?: string;
}

/** Payload of the SignalR "message" event (camelCase, matches the REST shape). */
export interface ChatEvent {
  conversation: Conversation;
  message: ConversationMessage;
}
