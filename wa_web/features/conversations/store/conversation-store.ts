import { create } from "zustand";
import type { Conversation } from "@/features/conversations/types";

interface ConversationUIState {
  /** The open conversation (full snapshot), or null for the empty state. */
  selected: Conversation | null;
  select: (conversation: Conversation | null) => void;
  /** Refresh the open conversation in place (used by the realtime hub when its snapshot changes). */
  patchSelected: (conversation: Conversation) => void;
}

export const useConversationStore = create<ConversationUIState>((set) => ({
  selected: null,
  select: (conversation) => set({ selected: conversation }),
  patchSelected: (conversation) =>
    set((state) =>
      state.selected && state.selected.id === conversation.id ? { selected: conversation } : state,
    ),
}));
