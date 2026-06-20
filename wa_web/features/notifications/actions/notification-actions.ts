"use server";

import { createAction } from "@/lib/actions/wrapper";
import { NotificationService } from "@/features/notifications/services/notification-service";
import type { NotificationListParams } from "@/features/notifications/types";

const COMPANY_ADMIN = "CompanyAdmin";

export const getNotificationsAction = createAction(
  { name: "getNotificationsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (params: NotificationListParams) => NotificationService.getList(params)
);

export const markNotificationReadAction = createAction(
  { name: "markNotificationReadAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => NotificationService.markRead(id)
);

export const markAllNotificationsReadAction = createAction(
  { name: "markAllNotificationsReadAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => NotificationService.markAllRead()
);
