"use client";

import { useState } from "react";
import { formatDistanceToNow } from "date-fns";
import {
  Bell,
  CheckCheck,
  CheckCircle2,
  AlertTriangle,
  Clock,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import {
  useNotifications,
  useMarkNotificationRead,
  useMarkAllNotificationsRead,
} from "@/features/notifications/hooks/use-notifications";
import type { Notification, NotificationType } from "@/features/notifications/types";

const PAGE_SIZE = 20;

function typeIcon(type: NotificationType) {
  if (type === "TemplateApproved" || type === "CampaignCompleted")
    return <CheckCircle2 className="size-5 text-green-500 shrink-0" />;
  if (type === "CampaignFailed")
    return <AlertTriangle className="size-5 text-red-500 shrink-0" />;
  return <Clock className="size-5 text-amber-500 shrink-0" />;
}

function typeLabel(type: NotificationType): string {
  const map: Record<NotificationType, string> = {
    TemplateApproved: "Template",
    CampaignFailed: "Campaign",
    CampaignThrottled: "Campaign",
    CampaignCompleted: "Campaign",
    QuotaWarning3Days: "Quota",
    QuotaWarning2Days: "Quota",
    QuotaWarning1Day: "Quota",
    QuotaWarningToday: "Quota",
  };
  return map[type] ?? type;
}

function NotificationRow({
  notification,
  onRead,
}: {
  notification: Notification;
  onRead: (id: string) => void;
}) {
  return (
    <div
      className={cn(
        "flex items-start gap-4 rounded-xl border p-4 transition-colors",
        !notification.isRead
          ? "border-primary/20 bg-primary/5"
          : "border-transparent bg-muted/30"
      )}
    >
      <div className="mt-0.5">{typeIcon(notification.type)}</div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <p
            className={cn(
              "text-sm leading-snug",
              !notification.isRead && "font-semibold"
            )}
          >
            {notification.title}
          </p>
          <Badge variant="outline" className="shrink-0 text-[10px]">
            {typeLabel(notification.type)}
          </Badge>
          {!notification.isRead && (
            <span className="ml-auto shrink-0 size-2 rounded-full bg-primary" />
          )}
        </div>
        <p className="mt-1 text-sm text-muted-foreground">{notification.body}</p>
        <p className="mt-2 text-xs text-muted-foreground">
          {formatDistanceToNow(new Date(notification.createdAt), {
            addSuffix: true,
          })}
        </p>
      </div>
      {!notification.isRead && (
        <Button
          variant="ghost"
          size="sm"
          className="shrink-0 h-7 px-2 text-xs"
          onClick={() => onRead(notification.id)}
        >
          Mark read
        </Button>
      )}
    </div>
  );
}

function NotificationSkeleton() {
  return (
    <div className="flex items-start gap-4 rounded-xl border p-4">
      <Skeleton className="mt-0.5 size-5 rounded-full" />
      <div className="flex-1 space-y-2">
        <Skeleton className="h-4 w-2/3" />
        <Skeleton className="h-3 w-full" />
        <Skeleton className="h-3 w-1/4" />
      </div>
    </div>
  );
}

export function NotificationsView() {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useNotifications(page, PAGE_SIZE);
  const { mutate: markRead } = useMarkNotificationRead();
  const { mutate: markAllRead, isPending: markingAll } =
    useMarkAllNotificationsRead();

  const totalPages = data ? Math.ceil(data.totalCount / PAGE_SIZE) : 1;

  return (
    <div className="space-y-4">
      {/* Toolbar */}
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {data
            ? `${data.totalCount} notification${data.totalCount !== 1 ? "s" : ""}${data.unreadCount > 0 ? ` · ${data.unreadCount} unread` : ""}`
            : ""}
        </p>
        {(data?.unreadCount ?? 0) > 0 && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => markAllRead()}
            disabled={markingAll}
          >
            <CheckCheck className="mr-2 size-4" />
            Mark all as read
          </Button>
        )}
      </div>

      {/* List */}
      <div className="space-y-2">
        {isLoading ? (
          Array.from({ length: 5 }).map((_, i) => (
            <NotificationSkeleton key={i} />
          ))
        ) : !data?.items.length ? (
          <div className="flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed py-20 text-center">
            <Bell className="size-10 text-muted-foreground/40" />
            <div>
              <p className="text-sm font-medium">No notifications</p>
              <p className="mt-1 text-xs text-muted-foreground">
                You'll see campaign alerts, quota warnings, and template approvals here.
              </p>
            </div>
          </div>
        ) : (
          data.items.map((n) => (
            <NotificationRow key={n.id} notification={n} onRead={markRead} />
          ))
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-end gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page === 1 || isLoading}
          >
            <ChevronLeft className="size-4" />
            Previous
          </Button>
          <span className="text-sm text-muted-foreground">
            {page} / {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page === totalPages || isLoading}
          >
            Next
            <ChevronRight className="size-4" />
          </Button>
        </div>
      )}
    </div>
  );
}
