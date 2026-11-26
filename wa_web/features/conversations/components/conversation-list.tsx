"use client";

import { useCallback, useMemo, useState } from "react";
import { Search, Plus, Inbox as InboxIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Spinner } from "@/components/ui/spinner";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { InputGroup, InputGroupAddon, InputGroupInput } from "@/components/ui/input-group";
import { useDebounce } from "@/hooks/use-debounce";
import { useConversations } from "@/features/conversations/hooks/use-conversations";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import { useBotAccess } from "@/features/bot/hooks/use-bot-access";
import type { Conversation } from "@/features/conversations/types";
import { ConversationListItem } from "./conversation-list-item";
import { NewConversationDialog } from "./new-conversation-dialog";
import { useOpenFromQuery } from "@/lib/hooks/use-open-from-query";

type Filter = "all" | "unread" | "needs-you";

/** Chats the bot handed to the team — someone has to answer these. */
const needsYou = (c: Conversation) => c.botState === "Paused" && !c.botPausedByAgent;

export function ConversationList() {
  const [search, setSearch] = useState("");
  const debounced = useDebounce(search, 300);
  const { data: conversations, isLoading, isError } = useConversations(debounced);
  const selected = useConversationStore((s) => s.selected);
  const [composeOpen, setComposeOpen] = useState(false);
  useOpenFromQuery("new", useCallback(() => setComposeOpen(true), []));
  const [filter, setFilter] = useState<Filter>("all");
  const botAccess = useBotAccess();
  const botOn = botAccess.chatbot && botAccess.platformEnabled;

  const counts = useMemo(() => {
    const list = conversations ?? [];
    return { unread: list.filter((c) => c.unreadCount > 0).length, needsYou: list.filter(needsYou).length };
  }, [conversations]);

  const visible = useMemo(() => {
    const list = conversations ?? [];
    if (filter === "unread") return list.filter((c) => c.unreadCount > 0);
    if (filter === "needs-you") return list.filter(needsYou);
    return list;
  }, [conversations, filter]);

  return (
    <div className="flex h-full flex-col border-r bg-card">
      {/* Same 64px height as the thread header, so both halves share one baseline. */}
      <div className="flex h-16 shrink-0 items-center justify-between gap-3 border-b px-4">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <InboxIcon className="size-[18px]" />
          </span>
          <div className="min-w-0 leading-tight">
            <h2 className="text-[17px] font-medium tracking-tight">Inbox</h2>
            <p className="flex items-center gap-1.5 truncate text-xs text-muted-foreground">
              {counts.unread > 0 ? (
                <>
                  <span className="size-1.5 shrink-0 rounded-full bg-primary" />
                  <span className="font-medium text-primary">
                    {counts.unread} unread
                  </span>
                </>
              ) : (
                "All caught up"
              )}
            </p>
          </div>
        </div>
        <Button size="sm" onClick={() => setComposeOpen(true)} className="h-8 shrink-0 rounded-lg px-3">
          <Plus data-icon="inline-start" /> New chat
        </Button>
      </div>

      <div className="shrink-0 space-y-2.5 border-b bg-muted/30 px-3 py-3">
        <InputGroup className="h-9 rounded-lg bg-card dark:bg-input/30">
          <InputGroupAddon className="pl-3">
            <Search className="size-4" />
          </InputGroupAddon>
          <InputGroupInput
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name or number"
            aria-label="Search conversations"
            className="h-full text-sm"
          />
        </InputGroup>

        <ToggleGroup
          type="single"
          value={filter}
          onValueChange={(v) => v && setFilter(v as Filter)}
          className="flex w-full gap-0.5 rounded-lg bg-muted p-0.5"
          aria-label="Filter conversations"
        >
          <FilterTab value="all" label="All" />
          <FilterTab value="unread" label="Unread" count={counts.unread} tone="green" />
          {botOn && <FilterTab value="needs-you" label="Needs you" count={counts.needsYou} tone="amber" />}
        </ToggleGroup>
      </div>

      <ScrollArea className="min-h-0 flex-1 [&_[data-slot=scroll-area-viewport]>div]:!block [&_[data-slot=scroll-area-viewport]>div]:!min-w-0">
        {isLoading ? (
          <div className="flex h-40 items-center justify-center">
            <Spinner className="size-5 text-muted-foreground" />
          </div>
        ) : isError ? (
          <p className="px-6 py-10 text-center text-sm text-muted-foreground">
            Conversations couldn&apos;t load. Refresh the page to try again.
          </p>
        ) : visible.length === 0 ? (
          <EmptyList filter={filter} searching={!!debounced} onNew={() => setComposeOpen(true)} />
        ) : (
          <ul className="flex w-full min-w-0 flex-col gap-0.5 px-2 pb-3">
            {visible.map((c) => (
              <ConversationListItem key={c.id} conversation={c} active={c.id === selected?.id} botOn={botOn} />
            ))}
          </ul>
        )}
      </ScrollArea>

      <NewConversationDialog open={composeOpen} onOpenChange={setComposeOpen} />
    </div>
  );
}

function FilterTab({
  value,
  label,
  count,
  tone,
}: {
  value: Filter;
  label: string;
  count?: number;
  tone?: "green" | "amber";
}) {
  return (
    <ToggleGroupItem
      value={value}
      className="h-7 flex-1 gap-1.5 rounded-md border-0 px-2 text-xs font-normal text-muted-foreground hover:bg-transparent hover:text-foreground data-[state=on]:bg-card data-[state=on]:font-medium data-[state=on]:text-foreground data-[state=on]:shadow-[0_1px_2px_rgb(0_0_0/0.08)]"
    >
      {label}
      {count ? (
        <span
          className={cn(
            "flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-semibold tabular-nums",
            tone === "amber" ? "bg-amber-500 text-white" : "bg-primary text-primary-foreground",
          )}
        >
          {count > 99 ? "99+" : count}
        </span>
      ) : null}
    </ToggleGroupItem>
  );
}

function EmptyList({ filter, searching, onNew }: { filter: Filter; searching: boolean; onNew: () => void }) {
  const text = searching
    ? "No chats match that name or number."
    : filter === "unread"
      ? "Nothing unread. New messages show up here first."
      : filter === "needs-you"
        ? "The bot hasn't handed any chats to your team."
        : "When a customer messages your WhatsApp number, the chat appears here.";

  return (
    <div className="flex flex-col items-center gap-3 px-6 py-14 text-center">
      <span className="flex size-11 items-center justify-center rounded-full bg-primary/10 text-primary">
        <InboxIcon className="size-5" />
      </span>
      <p className="max-w-[16rem] text-sm text-muted-foreground">{text}</p>
      {filter === "all" && !searching && (
        <Button variant="outline" size="sm" onClick={onNew} className="rounded-full">
          Start a chat
        </Button>
      )}
    </div>
  );
}
