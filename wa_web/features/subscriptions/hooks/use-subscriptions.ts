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
  getSubscriptionsAction,
  assignSubscriptionAction,
  changePlanAction,
  cancelSubscriptionAction,
  addPackageAction,
  getPackageHistoryAction,
  getSubscriptionHistoryAction,
} from "@/features/subscriptions/actions/subscription-actions";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import { getPlansAction } from "@/features/plans/actions/plan-actions";
import { getPackagesAction } from "@/features/packages/actions/package-actions";
import {
  useAssignSubscriptionDialog,
  useChangePlanDialog,
  useCancelSubscriptionDialog,
  useAddPackageDialog,
} from "@/features/subscriptions/store/subscription-store";
import type {
  AssignSubscriptionInput,
  ChangePlanInput,
  AddPackageInput,
} from "@/features/subscriptions/schema/subscription-schema";

// ── DataTable source ─────────────────────────────────────────────────────
export function useSubscriptionDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.subscriptions.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getSubscriptionsAction({
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
(useSubscriptionDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Picker sources (active companies / active plans) ─────────────────────
export function useCompanyOptions(enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.companies.all, "active-options"] as const,
    queryFn: async () => {
      const res = await getCompaniesAction({
        page: 1,
        pageSize: 100,
        sortBy: "name",
        sortOrder: "asc",
      });
      if (!res.success) throw new Error(res.error);
      return res.data.items.filter((c) => c.isActive).map((c) => ({ id: c.id, name: c.name }));
    },
    enabled,
    staleTime: 60_000,
  });
}

export function usePlanOptions(enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.plans.all, "active-options"] as const,
    queryFn: async () => {
      const res = await getPlansAction({
        page: 1,
        pageSize: 100,
        sortBy: "name",
        sortOrder: "asc",
      });
      if (!res.success) throw new Error(res.error);
      return res.data.items
        .filter((p) => p.isActive)
        .map((p) => ({ id: p.id, name: p.name, quota: p.monthlyMessageQuota }));
    },
    enabled,
    staleTime: 60_000,
  });
}

export function usePackageOptions(enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.packages.all, "active-options"] as const,
    queryFn: async () => {
      const res = await getPackagesAction({
        page: 1,
        pageSize: 100,
        sortBy: "name",
        sortOrder: "asc",
      });
      if (!res.success) throw new Error(res.error);
      return res.data.items
        .filter((p) => p.isActive)
        .map((p) => ({
          id: p.id,
          name: p.name,
          extraMessages: p.extraMessages,
          price: p.price,
          currency: p.currency,
        }));
    },
    enabled,
    staleTime: 60_000,
  });
}

// ── Package history (read) ───────────────────────────────────────────────
export function usePackageHistory(companyId: string | null) {
  return useQuery({
    queryKey: queryKeys.subscriptions.packageHistory(companyId ?? ""),
    queryFn: async () => {
      const res = await getPackageHistoryAction(companyId!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!companyId,
  });
}

// ── Subscription (subscribe) history (read) ───────────────────────────────
export function useSubscriptionHistory(companyId: string | null) {
  return useQuery({
    queryKey: queryKeys.subscriptions.subscriptionHistory(companyId ?? ""),
    queryFn: async () => {
      const res = await getSubscriptionHistoryAction(companyId!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!companyId,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useAssignSubscription() {
  const qc = useQueryClient();
  const { close } = useAssignSubscriptionDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: AssignSubscriptionInput) => {
      const res = await assignSubscriptionAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.subscriptions.all });
      setFieldErrors(null);
      close();
      toast.success("Subscription assigned successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Subscription", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useChangePlan() {
  const qc = useQueryClient();
  const { close } = useChangePlanDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ companyId, data }: { companyId: string; data: ChangePlanInput }) => {
      const res = await changePlanAction(companyId, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.subscriptions.all });
      setFieldErrors(null);
      close();
      toast.success("Plan changed successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Subscription", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useCancelSubscription() {
  const qc = useQueryClient();
  const { close } = useCancelSubscriptionDialog();

  return useMutation({
    mutationFn: async (companyId: string) => {
      const res = await cancelSubscriptionAction(companyId);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.subscriptions.all });
      close();
      toast.success("Subscription cancelled");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Subscription", "delete"),
  });
}

export function useAddPackage() {
  const qc = useQueryClient();
  const { close } = useAddPackageDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ companyId, data }: { companyId: string; data: AddPackageInput }) => {
      const res = await addPackageAction(companyId, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (_data, { companyId }) => {
      qc.invalidateQueries({ queryKey: queryKeys.subscriptions.all });
      qc.invalidateQueries({ queryKey: queryKeys.subscriptions.packageHistory(companyId) });
      setFieldErrors(null);
      close();
      toast.success("Package added successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Package", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}
