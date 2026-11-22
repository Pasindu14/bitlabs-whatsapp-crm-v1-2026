"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getBotProfileAction,
  getBotPromptAction,
  saveBotProfileAction,
  getBotFaqsAction,
  createBotFaqAction,
  updateBotFaqAction,
  deleteBotFaqAction,
  getBotLinksAction,
  createBotLinkAction,
  updateBotLinkAction,
  deleteBotLinkAction,
  previewBotAction,
} from "@/features/bot/actions/bot-actions";
import type {
  SaveBotFaqInput,
  SaveBotLinkInput,
  SaveBotProfileInput,
} from "@/features/bot/schema/bot-schema";
import type { BotPreviewTurn } from "@/features/bot/types";

// ── The profile ───────────────────────────────────────────────────────────
// A single record, so no list/detail split — the same shape as useCalendarSettings.
// The API returns defaults rather than a 404 when nothing is saved yet, so there is
// no "not configured" error state to handle here.
export function useBotProfile() {
  return useQuery({
    queryKey: queryKeys.botProfile.profile(),
    queryFn: async () => {
      const res = await getBotProfileAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

/**
 * What the model is actually given. Always refetched when the tab opens, because answers and links are
 * saved through their own endpoints and would otherwise leave a stale prompt on screen.
 */
export function useBotPrompt() {
  return useQuery({
    queryKey: queryKeys.botProfile.prompt(),
    queryFn: async () => {
      const res = await getBotPromptAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 0,
    refetchOnMount: "always",
  });
}

export function useSaveBotProfile() {
  const qc = useQueryClient();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: SaveBotProfileInput) => {
      const res = await saveBotProfileAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.all });
      setFieldErrors(null);
      toast.success("Assistant updated");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Assistant", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

// ── Prepared answers ──────────────────────────────────────────────────────
export function useBotFaqs() {
  return useQuery({
    queryKey: queryKeys.botProfile.faqs(),
    queryFn: async () => {
      const res = await getBotFaqsAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useCreateBotFaq() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (data: SaveBotFaqInput) => {
      const res = await createBotFaqAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.faqs() });
      toast.success("Answer added");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Answer", "create"),
  });
}

export function useUpdateBotFaq() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: SaveBotFaqInput }) => {
      const res = await updateBotFaqAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.faqs() });
      toast.success("Answer updated");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Answer", "update"),
  });
}

export function useDeleteBotFaq() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteBotFaqAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.faqs() });
      toast.success("Answer removed");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Answer", "delete"),
  });
}

// ── Shareable links ───────────────────────────────────────────────────────
export function useBotLinks() {
  return useQuery({
    queryKey: queryKeys.botProfile.links(),
    queryFn: async () => {
      const res = await getBotLinksAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useCreateBotLink() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (data: SaveBotLinkInput) => {
      const res = await createBotLinkAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.links() });
      toast.success("Link added");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Link", "create"),
  });
}

export function useUpdateBotLink() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: SaveBotLinkInput }) => {
      const res = await updateBotLinkAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.links() });
      toast.success("Link updated");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Link", "update"),
  });
}

export function useDeleteBotLink() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteBotLinkAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.botProfile.links() });
      toast.success("Link removed");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Link", "delete"),
  });
}

// ── The dry run ───────────────────────────────────────────────────────────
// Deliberately NOT a query: every run costs a model call, so it must only ever fire
// when someone presses send. Nothing here is cached or refetched.
export function usePreviewBot() {
  return useMutation({
    mutationFn: async (turns: BotPreviewTurn[]) => {
      const res = await previewBotAction(turns);
      if (!res.success) throw res;
      return res.data;
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Preview", "create"),
  });
}
