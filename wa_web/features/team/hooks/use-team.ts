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
  getTeamAction,
  getTeamMemberByIdAction,
  createTeamMemberAction,
  updateTeamMemberAction,
  resetTeamPasswordAction,
  activateTeamMemberAction,
  deactivateTeamMemberAction,
} from "@/features/team/actions/team-actions";
import {
  useCreateTeamDialog,
  useEditTeamDialog,
  useResetTeamPasswordDialog,
  useActivateTeamDialog,
  useDeactivateTeamDialog,
} from "@/features/team/store/team-store";
import type {
  CreateTeamMemberInput,
  UpdateTeamMemberInput,
  ResetTeamPasswordInput,
} from "@/features/team/schema/team-schema";

// ── DataTable source ─────────────────────────────────────────────────────
// A useQuery hook (NOT a plain fn) so mutations can invalidate team.all and
// the table refetches automatically — no manual refresh.
export function useTeamDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.team.list({ page, pageSize, search, sortBy, sortOrder }),
    queryFn: async () => {
      const res = await getTeamAction({
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
(useTeamDataTable as unknown as Record<string, unknown>).isQueryHook = true;

// ── Single member (edit dialog) ──────────────────────────────────────────
export function useTeamMember(id: string | null) {
  return useQuery({
    queryKey: queryKeys.team.detail(id ?? ""),
    queryFn: async () => {
      const res = await getTeamMemberByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────
export function useCreateTeamMember() {
  const qc = useQueryClient();
  const { close } = useCreateTeamDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateTeamMemberInput) => {
      const res = await createTeamMemberAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.team.all });
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

export function useUpdateTeamMember() {
  const qc = useQueryClient();
  const { close } = useEditTeamDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateTeamMemberInput }) => {
      const res = await updateTeamMemberAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.team.all });
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

export function useResetTeamPassword() {
  const { close } = useResetTeamPasswordDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: ResetTeamPasswordInput }) => {
      const res = await resetTeamPasswordAction(id, data);
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

export function useActivateTeamMember() {
  const qc = useQueryClient();
  const { close } = useActivateTeamDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await activateTeamMemberAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.team.all });
      close();
      toast.success("User activated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "user", "activate"),
  });
}

export function useDeactivateTeamMember() {
  const qc = useQueryClient();
  const { close } = useDeactivateTeamDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deactivateTeamMemberAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.team.all });
      close();
      toast.success("User deactivated successfully");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "user", "deactivate"),
  });
}
