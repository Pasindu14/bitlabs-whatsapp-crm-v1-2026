"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getNotificationsAction,
  markNotificationReadAction,
  markAllNotificationsReadAction,
} from "@/features/notifications/actions/notification-actions";

export function useNotifications(page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.notifications.list({ page, pageSize }),
    queryFn: async () => {
      const res = await getNotificationsAction({ page, pageSize });
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    refetchInterval: 30_000,
  });
}

export function useNotificationBell() {
  return useQuery({
    queryKey: queryKeys.notifications.list({ page: 1, pageSize: 5 }),
    queryFn: async () => {
      const res = await getNotificationsAction({ page: 1, pageSize: 5 });
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    refetchInterval: 30_000,
  });
}

export function useMarkNotificationRead() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await markNotificationReadAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.notifications.all });
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Notification", "update"),
  });
}

export function useMarkAllNotificationsRead() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const res = await markAllNotificationsReadAction();
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.notifications.all });
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Notification", "update"),
  });
}
