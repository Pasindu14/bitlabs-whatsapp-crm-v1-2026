"use client";

import { useMutation, useQuery, useQueryClient, keepPreviousData } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getConversationsAction,
  getConversationMessagesAction,
  sendConversationMessageAction,
  markConversationReadAction,
  startConversationAction,
  deleteConversationMessageAction,
  setConversationBotStateAction,
} from "@/features/conversations/actions/conversation-actions";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import type { StartConversationInput } from "@/features/conversations/schema/conversation-schema";
import type { ConversationMessage } from "@/features/conversations/types";

export function useConversations(search: string) {
  return useQuery({
    queryKey: queryKeys.conversations.list({ search }),
    queryFn: async () => {
      const res = await getConversationsAction({ page: 1, pageSize: 50, search: search || undefined });
      if (!res.success) throw new Error(res.error);
      return res.data.items;
    },
    placeholderData: keepPreviousData,
    staleTime: 10_000,
  });
}

export function useConversationMessages(conversationId: string | null) {
  return useQuery({
    queryKey: queryKeys.conversations.messages(conversationId ?? "none"),
    queryFn: async () => {
      const res = await getConversationMessagesAction(conversationId!, 1, 50);
      if (!res.success) throw new Error(res.error);
      // API returns newest-first (page 1 = latest); reverse so the thread reads oldest → newest.
      return [...res.data.items].reverse();
    },
    enabled: !!conversationId,
    // Realtime only covers the time the inbox is open — coming back to it must always refetch.
    staleTime: 0,
  });
}

export function useSendConversationMessage(conversationId: string) {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (body: string) => {
      const res = await sendConversationMessageAction(conversationId, { body });
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.messages(conversationId) });
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Message", "send"),
  });
}

/**
 * Sends a file (image, audio, video or document) in the thread, with the typed text as its caption. Goes
 * through the Next proxy because a server action can't carry a File cleanly. The new bubble arrives via the
 * realtime event; invalidating covers a missed one.
 */
export function useSendConversationMedia(conversationId: string) {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ file, caption }: { file: File; caption: string }) => {
      const fd = new FormData();
      fd.append("file", file);
      if (caption.trim()) fd.append("caption", caption.trim());
      const res = await fetch(`/api/conversations/${conversationId}/media`, { method: "POST", body: fd });
      const json = await res.json().catch(() => null);
      if (!res.ok) throw new Error(json?.error ?? "Could not send the file.");
      return json?.message as ConversationMessage | null;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.messages(conversationId) });
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
    },
    onError: (error: Error) => {
      toast.error(error.message);
      // A refused send can still leave a "Not delivered" bubble behind.
      qc.invalidateQueries({ queryKey: queryKeys.conversations.messages(conversationId) });
    },
  });
}

export function useMarkConversationRead() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (conversationId: string) => {
      const res = await markConversationReadAction(conversationId);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
    },
  });
}

export function useDeleteConversationMessage(conversationId: string) {
  const qc = useQueryClient();
  const key = queryKeys.conversations.messages(conversationId);

  return useMutation({
    mutationFn: async (messageId: string) => {
      const res = await deleteConversationMessageAction(conversationId, messageId);
      if (!res.success) throw res;
      return res.data;
    },
    // Optimistically drop the bubble so it disappears instantly (WhatsApp-style); roll back on failure.
    onMutate: async (messageId: string) => {
      await qc.cancelQueries({ queryKey: key });
      const previous = qc.getQueryData<ConversationMessage[]>(key);
      qc.setQueryData<ConversationMessage[]>(key, (old) =>
        old ? old.filter((m) => m.id !== messageId) : old,
      );
      return { previous };
    },
    onError: (error: ActionFailure, _messageId, context) => {
      if (context?.previous) qc.setQueryData(key, context.previous);
      handleErrorToast(error, "Message", "delete");
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
    },
    onSettled: () => {
      qc.invalidateQueries({ queryKey: key });
    },
  });
}

export function useStartConversation() {
  const qc = useQueryClient();
  const select = useConversationStore((s) => s.select);

  return useMutation({
    mutationFn: async (input: StartConversationInput) => {
      const res = await startConversationAction(input);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (conversation) => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
      select(conversation); // open the freshly created thread
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Conversation", "start"),
  });
}

/**
 * Takes the thread off the bot, or hands it back.
 *
 * The API also pauses the bot automatically the moment an agent sends a message, so most takeovers
 * never come through here — this is for claiming a thread before saying anything, and for giving it
 * back once the conversation is done.
 */
export function useSetConversationBotState() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({
      conversationId,
      paused,
      resumeAfterMinutes,
    }: {
      conversationId: string;
      paused: boolean;
      resumeAfterMinutes?: number;
    }) => {
      const res = await setConversationBotStateAction(conversationId, paused, resumeAfterMinutes);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (conversation) => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
      // Patch the open thread immediately so the banner flips without waiting for a refetch.
      useConversationStore.getState().patchSelected(conversation);
      toast.success(conversation.botState === "Paused" ? "You're handling this chat" : "Back to the bot");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Chat", "update"),
  });
}
