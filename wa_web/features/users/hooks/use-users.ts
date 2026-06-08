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
  getUsersAction,
  getUserByIdAction,
  createUserAction,
  updateUserAction,
  resetUserPasswordAction,
  activateUserAction,
  deactivateUserAction,
} from "@/features/users/actions/user-actions";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import {
  useCreateUserDialog,
  useEditUserDialog,
  useResetPasswordDialog,
  useActivateUserDialog,
  useDeactivateUserDialog,
} from "@/features/users/store/user-store";
import type {
  CreateUserInput,
  UpdateUserInput,
  ResetPasswordInput,
} from "@/features/users/schema/user-schema";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate users.all and
// the table refetches automatically — no manual refresh.
export function useUserDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.users.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getUsersAction({
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
(useUserDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single user (edit dialog) ────────────────────────────────────────────
export function useUser(id: string | null) {
  return useQuery({
    queryKey: queryKeys.users.detail(id ?? ""),
    queryFn: async () => {
      const res = await getUserByIdAction(id!);
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
export function useCreateUser() {
  const qc = useQueryClient();
  const { close } = useCreateUserDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateUserInput) => {
      const res = await createUserAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.users.all });
      setFieldErrors(null);
      close();
      toast.success("User created successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "user", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateUser() {
  const qc = useQueryClient();
  const { close } = useEditUserDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateUserInput }) => {
      const res = await updateUserAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.users.all });
      setFieldErrors(null);
      close();
      toast.success("User updated successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "user", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useResetUserPassword() {
  const { close } = useResetPasswordDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: ResetPasswordInput }) => {
      const res = await resetUserPasswordAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      setFieldErrors(null);
      close();
      toast.success("Password reset successfully");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "password", "reset");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useActivateUser() {
  const qc = useQueryClient();
  const { close } = useActivateUserDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activateUserAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.users.all });
      close();
      toast.success("User activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "user", "activate"),
  });
}

export function useDeactivateUser() {
  const qc = useQueryClient();
  const { close } = useDeactivateUserDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivateUserAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.users.all });
      close();
      toast.success("User deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "user", "deactivate"),
  });
}
