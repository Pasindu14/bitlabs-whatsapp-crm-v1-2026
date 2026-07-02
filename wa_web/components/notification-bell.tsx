"use client";

import { useSession } from "next-auth/react";
import Link from "next/link";
import { Bell, CheckCheck, Megaphone, AlertTriangle, Clock, CheckCircle2 } from "lucide-react";
import { formatDistanceToNow } from "date-fns";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";
import { useNotificationBell, useMarkNotificationRead, useMarkAllNotificationsRead } from "@/features/notifications/hooks/use-notifications";
import type { Notification, NotificationType } from "@/features/notifications/types";

function typeIcon(type: NotificationType) {
  if (type === "TemplateApproved" || type === "CampaignCompleted")
    return <CheckCircle2 className="size-4 text-green-500 shrink-0" />;
  if (type === "CampaignFailed") return <AlertTriangle className="size-4 text-red-500 shrink-0" />;
  return <Clock className="size-4 text-amber-500 shrink-0" />;
}

function NotificationItem({ notification, onRead }: { notification: Notification; onRead: (id: string) => void }) {
  return (
    <button
      onClick={() => { if (!notification.isRead) onRead(notification.id); }}
      className={cn(
        "flex w-full items-start gap-3 px-4 py-3 text-left transition-colors hover:bg-muted/50",
        !notification.isRead && "bg-muted/30"
      )}
    >
      <div className="mt-0.5">{typeIcon(notification.type)}</div>
      <div className="flex-1 min-w-0">
        <p className={cn("text-sm leading-snug", !notification.isRead && "font-medium")}>
          {notification.title}
        </p>
        <p className="mt-0.5 text-xs text-muted-foreground line-clamp-2">{notification.body}</p>
        <p className="mt-1 text-[10px] text-muted-foreground">
          {formatDistanceToNow(new Date(notification.createdAt), { addSuffix: true })}
        </p>
      </div>
      {!notification.isRead && (
        <span className="mt-1.5 size-2 shrink-0 rounded-full bg-primary" />
      )}
    </button>
  );
}

export function NotificationBell() {
  const { data: session } = useSession();
  const { data, isLoading } = useNotificationBell();
  const { mutate: markRead } = useMarkNotificationRead();
  const { mutate: markAllRead, isPending: markingAll } = useMarkAllNotificationsRead();

  if (session?.user?.role !== "CompanyAdmin") return null;

  const unread = data?.unreadCount ?? 0;

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="icon" className="relative h-8 w-8">
          <Bell className="size-4" />
          {unread > 0 && (
            <span className="absolute -right-0.5 -top-0.5 flex size-4 items-center justify-center rounded-full bg-destructive text-[10px] font-bold text-destructive-foreground">
              {unread > 9 ? "9+" : unread}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-0" sideOffset={8}>
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <span className="text-sm font-semibold">Notifications</span>
          {unread > 0 && (
            <Button
              variant="ghost"
              size="sm"
              className="h-auto px-2 py-1 text-xs"
              onClick={() => markAllRead()}
              disabled={markingAll}
            >
              <CheckCheck className="mr-1 size-3" />
              Mark all read
            </Button>
          )}
        </div>

        {/* Body */}
        <div className="max-h-[360px] overflow-y-auto overscroll-contain">
          {isLoading ? (
            <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
              Loading…
            </div>
          ) : !data?.items.length ? (
            <div className="flex flex-col items-center justify-center gap-2 py-10 text-center">
              <Bell className="size-8 text-muted-foreground/40" />
              <p className="text-sm text-muted-foreground">No notifications yet</p>
            </div>
          ) : (
            <div className="divide-y">
              {data.items.map((n) => (
                <NotificationItem key={n.id} notification={n} onRead={markRead} />
              ))}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="border-t px-4 py-2">
          <Link href="/notifications">
            <Button variant="ghost" size="sm" className="w-full text-xs">
              View all notifications
            </Button>
          </Link>
        </div>
      </PopoverContent>
    </Popover>
  );
}
