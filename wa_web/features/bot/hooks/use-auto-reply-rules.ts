"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getAutoReplyRulesAction,
  getAutoReplyRuleByIdAction,
  createAutoReplyRuleAction,
  updateAutoReplyRuleAction,
  deleteAutoReplyRuleAction,
} from "@/features/bot/actions/bot-actions";
import {
  useCreateRuleDialog,
  useEditRuleDialog,
  useDeleteRuleDialog,
} from "@/features/bot/store/bot-store";
import type {
  CreateAutoReplyRuleInput,
  UpdateAutoReplyRuleInput,
} from "@/features/bot/schema/bot-schema";

// ── The rule list ─────────────────────────────────────────────────────────
// Unpaged: a company's rule set is small, and the API already returns it in the
// same order the engine evaluates it, which is the order the screen must show.
export function useAutoReplyRules() {
  return useQuery({
    queryKey: queryKeys.autoReplyRules.list(),
    queryFn: async () => {
      const res = await getAutoReplyRulesAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

// ── Single rule (edit dialog) ─────────────────────────────────────────────
export function useAutoReplyRule(id: string | null) {
  return useQuery({
    queryKey: queryKeys.autoReplyRules.detail(id ?? ""),
    queryFn: async () => {
      const res = await getAutoReplyRuleByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ─────────────────────────────────────────────────────────────
export function useCreateAutoReplyRule() {
  const qc = useQueryClient();
  const { close } = useCreateRuleDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateAutoReplyRuleInput) => {
      const res = await createAutoReplyRuleAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.autoReplyRules.all });
      setFieldErrors(null);
      close();
      toast.success("Rule created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Rule", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateAutoReplyRule() {
  const qc = useQueryClient();
  const { close } = useEditRuleDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateAutoReplyRuleInput }) => {
      const res = await updateAutoReplyRuleAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.autoReplyRules.all });
      setFieldErrors(null);
      close();
      toast.success("Rule updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Rule", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useDeleteAutoReplyRule() {
  const qc = useQueryClient();
  const { close } = useDeleteRuleDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteAutoReplyRuleAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.autoReplyRules.all });
      close();
      toast.success("Rule deleted successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Rule", "delete"),
  });
}

/** Toggling enabled/disabled is a full update — the API has no dedicated endpoint. */
export function useToggleAutoReplyRule() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateAutoReplyRuleInput }) => {
      const res = await updateAutoReplyRuleAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: queryKeys.autoReplyRules.all });
      toast.success(rule.isEnabled ? "Rule enabled" : "Rule disabled");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Rule", "update"),
  });
}
