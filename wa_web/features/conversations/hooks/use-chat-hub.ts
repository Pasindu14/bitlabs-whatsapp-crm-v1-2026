"use client";

import { useEffect, useRef } from "react";
import { useSession } from "next-auth/react";
import { useQueryClient } from "@tanstack/react-query";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { queryKeys } from "@/lib/hooks/query-keys";
import { useConversationStore } from "@/features/conversations/store/conversation-store";
import type {
  BotStateChangedEvent,
  BotTypingEvent,
  ChatEvent,
  ConversationMessage,
  MessageDeletedEvent,
  MessageStatusEvent,
} from "@/features/conversations/types";

/**
 * Subscribes to the company's real-time chat channel. On every "message" event (inbound from a customer
 * or outbound echoed back) it invalidates the inbox list and the affected thread, so TanStack Query
 * refetches the authoritative data. The JWT is read from the session and supplied via SignalR's
 * accessTokenFactory (WebSockets can't send an Authorization header on the handshake).
 *
 * @param apiUrl Browser-reachable API origin (passed from the server component — API_URL is server-only).
 */
export function useChatHub(apiUrl: string) {
  const { data: session } = useSession();
  const token = session?.user?.accessToken;
  const tokenRef = useRef<string | undefined>(token);
  const qc = useQueryClient();

  // Keep the freshest token reachable from the long-lived accessTokenFactory closure.
  useEffect(() => {
    tokenRef.current = token;
  }, [token]);

  useEffect(() => {
    if (!apiUrl || !token) return;

    const connection = new HubConnectionBuilder()
      .withUrl(`${apiUrl}/hubs/chat`, { accessTokenFactory: () => tokenRef.current ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("message", (event: ChatEvent) => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
      const conversationId = event?.conversation?.id;
      if (conversationId) {
        qc.invalidateQueries({ queryKey: queryKeys.conversations.messages(conversationId) });
        // Keep the open thread's header + 24h-window state fresh (inbound resets the window).
        useConversationStore.getState().patchSelected(event.conversation);
        // A delivered message ends the composing state, whatever the server did or did not send —
        // the reply arriving IS the end of it.
        useConversationStore.getState().setBotTyping(conversationId, false);
      }
    });

    // The assistant is working on a reply. Several seconds pass between the customer's message and
    // the answer (model call, tool round-trips, the deliberate read delay), which otherwise looks
    // like nothing is happening.
    connection.on("botTyping", (event: BotTypingEvent) => {
      if (event?.conversationId) {
        useConversationStore.getState().setBotTyping(event.conversationId, !!event.isTyping);
      }
    });

    // Another agent took the thread over, or handed it back — flip the banner without a refresh so
    // two agents do not start typing over each other.
    connection.on("botStateChanged", (event: BotStateChangedEvent) => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
      if (event?.conversation) useConversationStore.getState().patchSelected(event.conversation);
    });

    // Another agent deleted a message ("delete for me") — drop it from any open thread + refresh the list.
    connection.on("messageDeleted", (event: MessageDeletedEvent) => {
      qc.invalidateQueries({ queryKey: queryKeys.conversations.lists() });
      const conversationId = event?.conversationId;
      if (conversationId) {
        qc.invalidateQueries({ queryKey: queryKeys.conversations.messages(conversationId) });
        if (event.conversation) useConversationStore.getState().patchSelected(event.conversation);
      }
    });

    // Meta reported a delivery status (sent/delivered/read/failed) — move the ticks on an open thread.
    // Patch the cached bubble so it flips instantly, then refetch to reconcile with the server.
    connection.on("messageStatus", (event: MessageStatusEvent) => {
      const conversationId = event?.conversationId;
      if (!conversationId) return;
      const key = queryKeys.conversations.messages(conversationId);
      qc.setQueryData<ConversationMessage[]>(key, (old) =>
        old?.map((m) =>
          m.id === event.messageId
            ? { ...m, status: event.status, statusAt: event.statusAt, errorMessage: event.errorMessage }
            : m,
        ),
      );
      qc.invalidateQueries({ queryKey: key });
    });

    // The server does not replay events, so anything sent while we were disconnected — the inbox
    // page was closed, or the socket dropped — is gone. Once (re)connected, refetch everything and
    // drop typing flags whose "done" event we may have missed.
    const resync = () => {
      useConversationStore.getState().clearBotTyping();
      qc.invalidateQueries({ queryKey: queryKeys.conversations.all });
    };
    connection.onreconnected(resync);

    // Realtime is an enhancement — the inbox still works via normal fetches — so a failed connect is a
    // warning, not an error (and won't trip Next's error overlay). Crucially we NEVER call stop() while
    // start() is still negotiating: under React Strict Mode's dev mount→unmount→remount that throws
    // "The connection was stopped during negotiation". Instead the cleanup waits for start() to settle,
    // then stops — so the first (torn-down) connection ends cleanly and the remount connects normally.
    const startPromise = connection.start().then(resync, (err: unknown) => {
      console.warn("ChatHub realtime unavailable:", err instanceof Error ? err.message : err);
    });

    return () => {
      startPromise.finally(() => {
        connection.stop().catch(() => {});
      });
    };
    // Depend on token presence (not value): a token refresh must not tear down the socket —
    // the factory reads the latest token from tokenRef on (re)connect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiUrl, !!token]);
}
