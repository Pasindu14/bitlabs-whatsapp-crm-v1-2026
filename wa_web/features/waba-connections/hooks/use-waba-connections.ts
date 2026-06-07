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
  getWabaConnectionsAction,
  getWabaConnectionByIdAction,
  createWabaConnectionAction,
  updateWabaConnectionAction,
  activateWabaConnectionAction,
  deactivateWabaConnectionAction,
} from "@/features/waba-connections/actions/waba-connection-actions";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import {
  useCreateWabaConnectionDialog,
  useEditWabaConnectionDialog,
  useActivateWabaConnectionDialog,
  useDeactivateWabaConnectionDialog,
} from "@/features/waba-connections/store/waba-connection-store";
import type {
  CreateWabaConnectionInput,
  UpdateWabaConnectionInput,
} from "@/features/waba-connections/schema/waba-connection-schema";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate wabaConnections.all
// and the table refetches automatically — no manual refresh.
export function useWabaConnectionDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.wabaConnections.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getWabaConnectionsAction({
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
(useWabaConnectionDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single connection (edit dialog) ──────────────────────────────────────
export function useWabaConnection(id: string | null) {
  return useQuery({
    queryKey: queryKeys.wabaConnections.detail(id ?? ""),
    queryFn: async () => {
      const res = await getWabaConnectionByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Active companies (Company picker source) ─────────────────────────────
// SuperAdmin already has access to the companies endpoint; we fetch a generous
// page and keep only active companies for the dropdown.
export function useActiveCompanies(enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.companies.all, "active-options"] as const,
    queryFn: async () => {
      const res = await getCompaniesAction({ page: 1, pageSize: 100, sortBy: "name", sortOrder: "asc" });
      if (!res.success) throw new Error(res.error);
      return res.data.items
        .filter((c) => c.isActive)
        .map((c) => ({ id: c.id, name: c.name }));
    },
    enabled,
    staleTime: 60_000,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreateWabaConnection() {
  const qc = useQueryClient();
  const { close } = useCreateWabaConnectionDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateWabaConnectionInput) => {
      const res = await createWabaConnectionAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.wabaConnections.all });
      setFieldErrors(null);
      close();
      toast.success("WABA connection created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "WABA connection", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateWabaConnection() {
  const qc = useQueryClient();
  const { close } = useEditWabaConnectionDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateWabaConnectionInput }) => {
      const res = await updateWabaConnectionAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.wabaConnections.all });
      setFieldErrors(null);
      close();
      toast.success("WABA connection updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "WABA connection", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivateWabaConnection() {
  const qc = useQueryClient();
  const { close } = useActivateWabaConnectionDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activateWabaConnectionAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.wabaConnections.all });
      close();
      toast.success("WABA connection activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "WABA connection", "activate"),
  });
}

export function useDeactivateWabaConnection() {
  const qc = useQueryClient();
  const { close } = useDeactivateWabaConnectionDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivateWabaConnectionAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.wabaConnections.all });
      close();
      toast.success("WABA connection deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "WABA connection", "deactivate"),
  });
}
