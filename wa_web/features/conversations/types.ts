export type ConversationStatus = "Open" | "Closed";
export type ConversationBotState = "Active" | "Paused";
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
  /** Whether the bot is answering this thread, or a human has taken it over. */
  botState: ConversationBotState;
  /** When the bot picks the thread back up on its own. Null means manual hand-back only. */
  botResumesAt: string | null;
  /** True when a person paused the bot here; false when the bot handed the chat over itself. */
  botPausedByAgent: boolean;
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
  /** True once the 90-day retention sweep removed the file — the message and caption stay. */
  mediaExpired?: boolean;
  /** Original file name — documents only. */
  mediaFileName?: string | null;
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

/** Payload of the SignalR "messageDeleted" event — a message was hidden via "delete for me". */
export interface MessageDeletedEvent {
  conversationId: string;
  messageId: string;
  conversation: Conversation;
}

/** Payload of the SignalR "botStateChanged" event — an agent took over, or handed back. */
export interface BotStateChangedEvent {
  conversation: Conversation;
}

/** Payload of the SignalR "botTyping" event — the assistant is composing a reply, or has stopped. */
export interface BotTypingEvent {
  conversationId: string;
  isTyping: boolean;
}

/** Payload of the SignalR "messageStatus" event — Meta reported an outbound message sent/delivered/read/failed. */
export interface MessageStatusEvent {
  conversationId: string;
  messageId: string;
  status: MessageStatus;
  statusAt: string | null;
  errorMessage: string | null;
}
