"use client";

import { useMutation, useQuery, useQueryClient, keepPreviousData } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import { getMessagesAction, sendMessageAction } from "@/features/messages/actions/message-actions";
import { getActiveWabaConnectionsAction } from "@/features/messages/actions/my-waba-connection-actions";
import { useSendMessageDialog } from "@/features/messages/store/message-store";
import type { SendMessageInput } from "@/features/messages/schema/message-schema";

export function useMessageDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.messages.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getMessagesAction({
        page,
        pageSize,
        search: search || undefined,
        sortBy: sortBy || undefined,
        sortOrder: (sortOrder as "asc" | "desc") || undefined,
      });
      if (!res.success) throw new Error(res.error);
      const { items, pagination } = res.data;
      return {
        success: true as const,
        data: items,
        pagination: {
          page: pagination.page,
          limit: pagination.pageSize,
          total_pages: pagination.totalPages,
          total_items: pagination.total,
        },
      };
    },
    placeholderData: keepPreviousData,
  });
}
(useMessageDataTable as unknown as Record<string, unknown>).isQueryHook = true;

export function useActiveWabaConnections() {
  return useQuery({
    queryKey: queryKeys.messages.myWabaConnections(),
    queryFn: async () => {
      const res = await getActiveWabaConnectionsAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    retry: 1,
    staleTime: 60_000,
  });
}

export function useSendMessage() {
  const qc = useQueryClient();
  const { close } = useSendMessageDialog();

  return useMutation({
    mutationFn: async ({ contactId, data }: { contactId: string; data: SendMessageInput }) => {
      const res = await sendMessageAction(contactId, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.messages.all });
      close();
      toast.success("Message sent successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Message", "send"),
  });
}
