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
  getCompaniesAction,
  getCompanyByIdAction,
  createCompanyAction,
  updateCompanyAction,
  activateCompanyAction,
  deactivateCompanyAction,
} from "@/features/companies/actions/company-actions";
import {
  useCreateCompanyDialog,
  useEditCompanyDialog,
  useActivateCompanyDialog,
  useDeactivateCompanyDialog,
} from "@/features/companies/store/company-store";
import type {
  CreateCompanyInput,
  UpdateCompanyInput,
} from "@/features/companies/schema/company-schema";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate companies.all
// and the table refetches automatically — no manual refresh.
export function useCompanyDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.companies.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getCompaniesAction({
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
(useCompanyDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single company (edit dialog) ─────────────────────────────────────────
export function useCompany(id: string | null) {
  return useQuery({
    queryKey: queryKeys.companies.detail(id ?? ""),
    queryFn: async () => {
      const res = await getCompanyByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreateCompany() {
  const qc = useQueryClient();
  const { close } = useCreateCompanyDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateCompanyInput) => {
      const res = await createCompanyAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.companies.all });
      setFieldErrors(null);
      close();
      toast.success("Company created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "company", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateCompany() {
  const qc = useQueryClient();
  const { close } = useEditCompanyDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateCompanyInput }) => {
      const res = await updateCompanyAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.companies.all });
      setFieldErrors(null);
      close();
      toast.success("Company updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "company", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivateCompany() {
  const qc = useQueryClient();
  const { close } = useActivateCompanyDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activateCompanyAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.companies.all });
      close();
      toast.success("Company activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "company", "activate"),
  });
}

export function useDeactivateCompany() {
  const qc = useQueryClient();
  const { close } = useDeactivateCompanyDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivateCompanyAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.companies.all });
      close();
      toast.success("Company deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "company", "deactivate"),
  });
}
