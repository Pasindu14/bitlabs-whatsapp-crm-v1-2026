"use client";

import { useState } from "react";
import { Search, MessageCircle, SquarePen } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Spinner } from "@/components/ui/spinner";
import { useDebounce } from "@/hooks/use-debounce";
import { useConversations } from "@/features/conversations/hooks/use-conversations";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import { ConversationListItem } from "./conversation-list-item";
import { NewConversationDialog } from "./new-conversation-dialog";

export function ConversationList() {
  const [search, setSearch] = useState("");
  const debounced = useDebounce(search, 300);
  const { data: conversations, isLoading, isError } = useConversations(debounced);
  const selected = useConversationStore((s) => s.selected);
  const [composeOpen, setComposeOpen] = useState(false);

  return (
    <div className="flex h-full flex-col border-r bg-card">
      <div className="shrink-0 border-b px-4 py-3">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-lg font-semibold tracking-tight">Inbox</h2>
          <Button
            variant="ghost"
            size="icon"
            className="size-8"
            onClick={() => setComposeOpen(true)}
            aria-label="New conversation"
          >
            <SquarePen className="size-4" />
          </Button>
        </div>
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name or number"
            className="pl-9"
          />
        </div>
      </div>

      <ScrollArea className="min-h-0 flex-1">
        {isLoading ? (
          <div className="flex h-40 items-center justify-center">
            <Spinner className="size-5 text-muted-foreground" />
          </div>
        ) : isError ? (
          <div className="px-4 py-10 text-center text-sm text-muted-foreground">
            Could not load conversations.
          </div>
        ) : !conversations || conversations.length === 0 ? (
          <div className="flex flex-col items-center gap-2 px-4 py-16 text-center">
            <MessageCircle className="size-8 text-muted-foreground/50" />
            <p className="text-sm font-medium">No conversations yet</p>
            <p className="text-xs text-muted-foreground">
              Incoming WhatsApp messages will appear here in real time.
            </p>
          </div>
        ) : (
          <ul className="divide-y">
            {conversations.map((c) => (
              <ConversationListItem key={c.id} conversation={c} active={c.id === selected?.id} />
            ))}
          </ul>
        )}
      </ScrollArea>

      <NewConversationDialog open={composeOpen} onOpenChange={setComposeOpen} />
    </div>
  );
}
