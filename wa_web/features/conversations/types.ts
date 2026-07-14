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
  /**
   * Inbound media. `mediaType` is null for plain text; when set ("image" | "video" | "audio" | "document" |
   * "sticker") the message carries media served from `/api/media/{id}`. `mediaReady` is false until the bytes
   * have been fetched from Meta — the UI shows a placeholder until the realtime "media ready" event flips it.
   */
  mediaType?: string | null;
  mediaMimeType?: string | null;
  mediaReady?: boolean;
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
