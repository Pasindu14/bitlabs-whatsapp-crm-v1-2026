"use client";

import {
  ResizablePanelGroup,
  ResizablePanel,
  ResizableHandle,
} from "@/components/ui/resizable";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import { useChatHub } from "@/features/conversations/hooks/use-chat-hub";
import { ConversationList } from "./conversation-list";
import { MessageThread } from "./message-thread";

/**
 * Two-pane WhatsApp inbox. `apiUrl` is threaded down from the server component (API_URL is server-only)
 * so the SignalR client can reach the hub from the browser.
 */
export function Inbox({ apiUrl }: { apiUrl: string }) {
  useChatHub(apiUrl);
  const selected = useConversationStore((s) => s.selected);

  return (
    <div className="h-[calc(100svh-3rem-1px)] w-full overflow-hidden">
      {/* Desktop: resizable two-pane */}
      <ResizablePanelGroup orientation="horizontal" className="hidden h-full md:flex">
        {/* v4 sizes: unitless strings are percentages (bare numbers would be pixels). */}
        <ResizablePanel defaultSize="34" minSize="26" maxSize="46" className="min-w-[290px]">
          <ConversationList />
        </ResizablePanel>
        <ResizableHandle withHandle />
        <ResizablePanel defaultSize="66">
          <MessageThread />
        </ResizablePanel>
      </ResizablePanelGroup>

      {/* Mobile: one pane at a time */}
      <div className="h-full md:hidden">{selected ? <MessageThread /> : <ConversationList />}</div>
    </div>
  );
}
