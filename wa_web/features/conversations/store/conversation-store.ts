import { create } from "zustand";
import type { Conversation } from "@/features/conversations/types";

/**
 * How long a "the assistant is working" flag survives without being cleared by the server.
 *
 * The server sends `false` on every exit path, but a job that is killed mid-flight never sends it —
 * and a spinner that never stops is worse than no spinner, because it makes a working inbox look
 * broken. Comfortably longer than a real reply (model call + tool round-trips + the read delay,
 * ~10s at the top end) and short enough that a stuck one clears while someone is still watching.
 */
const BOT_TYPING_TIMEOUT_MS = 45_000;

interface ConversationUIState {
  /** The open conversation (full snapshot), or null for the empty state. */
  selected: Conversation | null;
  select: (conversation: Conversation | null) => void;
  /** Refresh the open conversation in place (used by the realtime hub when its snapshot changes). */
  patchSelected: (conversation: Conversation) => void;

  /** Conversation ids the assistant is currently composing a reply for. */
  botTyping: Record<string, true>;
  setBotTyping: (conversationId: string, isTyping: boolean) => void;
  /** Drop every typing flag — used after a (re)connect, when any flag we hold may already be over. */
  clearBotTyping: () => void;
}

/** Timers live outside the store — they are not state and must not trigger a render. */
const typingTimers = new Map<string, ReturnType<typeof setTimeout>>();

export const useConversationStore = create<ConversationUIState>((set, get) => ({
  selected: null,
  select: (conversation) => set({ selected: conversation }),
  patchSelected: (conversation) =>
    set((state) =>
      state.selected && state.selected.id === conversation.id ? { selected: conversation } : state,
    ),

  botTyping: {},
  setBotTyping: (conversationId, isTyping) => {
    const existing = typingTimers.get(conversationId);
    if (existing) {
      clearTimeout(existing);
      typingTimers.delete(conversationId);
    }

    if (isTyping) {
      typingTimers.set(
        conversationId,
        setTimeout(() => {
          typingTimers.delete(conversationId);
          get().setBotTyping(conversationId, false);
        }, BOT_TYPING_TIMEOUT_MS),
      );
    }

    set((state) => {
      if (isTyping) {
        if (state.botTyping[conversationId]) return state;
        return { botTyping: { ...state.botTyping, [conversationId]: true } };
      }

      if (!state.botTyping[conversationId]) return state;
      const next = { ...state.botTyping };
      delete next[conversationId];
      return { botTyping: next };
    });
  },
  clearBotTyping: () => {
    typingTimers.forEach((timer) => clearTimeout(timer));
    typingTimers.clear();
    set({ botTyping: {} });
  },
}));
