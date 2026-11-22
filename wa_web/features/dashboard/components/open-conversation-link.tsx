"use client";

import Link from "next/link";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import type { Conversation } from "@/features/conversations/types";

/**
 * Opens the inbox with this conversation already selected. The inbox keeps its selection in a
 * client store that survives client-side navigation, so selecting before the route change is enough.
 */
export function OpenConversationLink({
  conversation,
  className,
  children,
}: {
  conversation: Conversation;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <Link
      href="/inbox"
      className={className}
      onClick={() => useConversationStore.getState().select(conversation)}
    >
      {children}
    </Link>
  );
}
