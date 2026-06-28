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
  getPackagesAction,
  getPackageByIdAction,
  createPackageAction,
  updatePackageAction,
  activatePackageAction,
  deactivatePackageAction,
} from "@/features/packages/actions/package-actions";
import {
  useCreatePackageDialog,
  useEditPackageDialog,
  useActivatePackageDialog,
  useDeactivatePackageDialog,
} from "@/features/packages/store/package-store";
import type {
  CreatePackageInput,
  UpdatePackageInput,
} from "@/features/packages/schema/package-schema";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate packages.all
// and the table refetches automatically — no manual refresh.
export function usePackageDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.packages.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getPackagesAction({
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
(usePackageDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single package (edit dialog) ───────────────────────────────────────────
export function usePackage(id: string | null) {
  return useQuery({
    queryKey: queryKeys.packages.detail(id ?? ""),
    queryFn: async () => {
      const res = await getPackageByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreatePackage() {
  const qc = useQueryClient();
  const { close } = useCreatePackageDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreatePackageInput) => {
      const res = await createPackageAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.packages.all });
      setFieldErrors(null);
      close();
      toast.success("Package created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Package", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdatePackage() {
  const qc = useQueryClient();
  const { close } = useEditPackageDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdatePackageInput }) => {
      const res = await updatePackageAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.packages.all });
      setFieldErrors(null);
      close();
      toast.success("Package updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Package", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivatePackage() {
  const qc = useQueryClient();
  const { close } = useActivatePackageDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activatePackageAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.packages.all });
      close();
      toast.success("Package activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Package", "activate"),
  });
}

export function useDeactivatePackage() {
  const qc = useQueryClient();
  const { close } = useDeactivatePackageDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivatePackageAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.packages.all });
      close();
      toast.success("Package deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Package", "deactivate"),
  });
}
