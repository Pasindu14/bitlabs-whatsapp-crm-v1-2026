"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient, keepPreviousData } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getTemplatesAction,
  getTemplateByIdAction,
  createTemplateAction,
  updateTemplateAction,
  submitTemplateAction,
  refreshTemplateStatusAction,
  deleteTemplateAction,
} from "@/features/templates/actions/template-actions";
import { useSubmitTemplateDialog, useDeleteTemplateDialog } from "@/features/templates/store/template-store";
import { toTemplateRow, type TemplatePayload } from "@/features/templates/types";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate templates.all and the
// table refetches automatically. Items are flattened to TemplateRow for the table.
export function useTemplateDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.templates.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getTemplatesAction({
        page,
        pageSize,
        search: search || undefined,
        sortBy: sortBy || undefined,
        sortOrder: (sortOrder as "asc" | "desc") || undefined,
      });
      if (!res.success) throw new Error(res.error);

      const { items, pagination } = res.data;
      return {
        success: true as const,
        data: items.map(toTemplateRow),
        pagination: {
          page: pagination.page,
          limit: pagination.pageSize,
          total_pages: pagination.totalPages,
          total_items: pagination.total,
        },
      };
    },
    placeholderData: keepPreviousData,
  });
}
(useTemplateDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single template (builder / detail) ───────────────────────────────────
export function useTemplate(id: string | null) {
  return useQuery({
    queryKey: queryKeys.templates.detail(id ?? ""),
    queryFn: async () => {
      const res = await getTemplateByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
// Create/Update expose fieldErrors (bound onto the builder inputs); navigation is the
// builder's concern (it awaits mutateAsync then routes), so these only invalidate + toast.
export function useCreateTemplate() {
  const qc = useQueryClient();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: TemplatePayload) => {
      const res = await createTemplateAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.templates.all });
      setFieldErrors(null);
      toast.success("Template saved as draft");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Template", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateTemplate() {
  const qc = useQueryClient();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: TemplatePayload }) => {
      const res = await updateTemplateAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (t) => {
      qc.invalidateQueries({ queryKey: queryKeys.templates.all });
      qc.invalidateQueries({ queryKey: queryKeys.templates.detail(t.id) });
      setFieldErrors(null);
      toast.success("Draft updated");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Template", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useSubmitTemplate() {
  const qc = useQueryClient();
  const { close } = useSubmitTemplateDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await submitTemplateAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.templates.all });
      close();
      toast.success("Submitted to WhatsApp — awaiting approval");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Template", "submit"),
  });
}

export function useRefreshTemplateStatus() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await refreshTemplateStatusAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (t) => {
      qc.invalidateQueries({ queryKey: queryKeys.templates.all });
      qc.invalidateQueries({ queryKey: queryKeys.templates.detail(t.id) });
      toast.success(`Status: ${t.status}`);
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Template", "refresh"),
  });
}

export function useDeleteTemplate() {
  const qc = useQueryClient();
  const { close } = useDeleteTemplateDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteTemplateAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.templates.all });
      close();
      toast.success("Template deleted");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Template", "delete"),
  });
}
