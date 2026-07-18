"use client";

import { useMutation, useQuery, useQueryClient, keepPreviousData } from "@tanstack/react-query";
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
