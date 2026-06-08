"use client";

import { useState } from "react";
import {
  useMutation,
  useQuery,
  useQueryClient,
  keepPreviousData,
} from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getPlansAction,
  getPlanByIdAction,
  createPlanAction,
  updatePlanAction,
  activatePlanAction,
  deactivatePlanAction,
} from "@/features/plans/actions/plan-actions";
import {
  useCreatePlanDialog,
  useEditPlanDialog,
  useActivatePlanDialog,
  useDeactivatePlanDialog,
} from "@/features/plans/store/plan-store";
import type {
  CreatePlanInput,
  UpdatePlanInput,
} from "@/features/plans/schema/plan-schema";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate plans.all
// and the table refetches automatically — no manual refresh.
export function usePlanDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.plans.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getPlansAction({
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
        data: items,
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
// Mark as a query hook so the DataTable calls it as a hook (enables auto-refresh).
(usePlanDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single plan (edit dialog) ────────────────────────────────────────────
export function usePlan(id: string | null) {
  return useQuery({
    queryKey: queryKeys.plans.detail(id ?? ""),
    queryFn: async () => {
      const res = await getPlanByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreatePlan() {
  const qc = useQueryClient();
  const { close } = useCreatePlanDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreatePlanInput) => {
      const res = await createPlanAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.plans.all });
      setFieldErrors(null);
      close();
      toast.success("Plan created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Plan", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdatePlan() {
  const qc = useQueryClient();
  const { close } = useEditPlanDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdatePlanInput }) => {
      const res = await updatePlanAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.plans.all });
      setFieldErrors(null);
      close();
      toast.success("Plan updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Plan", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivatePlan() {
  const qc = useQueryClient();
  const { close } = useActivatePlanDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activatePlanAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.plans.all });
      close();
      toast.success("Plan activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Plan", "activate"),
  });
}

export function useDeactivatePlan() {
  const qc = useQueryClient();
  const { close } = useDeactivatePlanDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivatePlanAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.plans.all });
      close();
      toast.success("Plan deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Plan", "deactivate"),
  });
}
