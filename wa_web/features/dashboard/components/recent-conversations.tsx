import Link from "next/link";
import { ArrowRight, MessageCircle } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type { Conversation } from "@/features/conversations/types";
import { initials, timeAgo } from "@/features/dashboard/format";
import { avatarTone } from "@/features/conversations/components/format";
import { cn } from "@/lib/utils";
import { OpenConversationLink } from "@/features/dashboard/components/open-conversation-link";

/** The latest chats, newest first, with unread counts. */
export function RecentConversations({ items }: { items: Conversation[] | null }) {
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle className="text-[15px]">Recent conversations</CardTitle>
        <CardDescription>Latest WhatsApp chats across your numbers.</CardDescription>
        <CardAction>
          <Button asChild variant="ghost" size="sm">
            <Link href="/inbox">
              Open inbox <ArrowRight data-icon="inline-end" />
            </Link>
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col">
        {items === null ? (
          <Empty text="Conversations could not be loaded." />
        ) : items.length === 0 ? (
          <Empty text="No conversations yet. They appear here when a customer messages you." />
        ) : (
          items.map((c) => (
            <OpenConversationLink
              key={c.id}
              conversation={c}
              className="-mx-2 flex items-center gap-3 rounded-md px-2 py-2 transition-colors hover:bg-muted/60"
            >
              <Avatar className="size-9">
                <AvatarFallback className={cn("text-[12px] font-medium", avatarTone(c.contactName || c.contactPhone))}>
                  {initials(c.contactName || c.contactPhone)}
                </AvatarFallback>
              </Avatar>
              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <p className="truncate font-medium">{c.contactName || c.contactPhone}</p>
                  <span className="shrink-0 text-[0.7rem] text-muted-foreground">{timeAgo(c.lastMessageAt)}</span>
                </div>
                <div className="flex items-center justify-between gap-2">
                  <p className="truncate text-muted-foreground">
                    {c.lastMessageDirection === "Outbound" && "You: "}
                    {c.lastMessageBody || "Media message"}
                  </p>
                  {c.unreadCount > 0 && (
                    <Badge className="h-4 min-w-4 shrink-0 rounded-full px-1 tabular-nums">{c.unreadCount}</Badge>
                  )}
                </div>
              </div>
            </OpenConversationLink>
          ))
        )}
      </CardContent>
    </Card>
  );
}

function Empty({ text }: { text: string }) {
  return (
    <div className="flex flex-col items-center gap-2 py-10 text-center text-muted-foreground">
      <MessageCircle className="size-5" />
      {text}
    </div>
  );
}
