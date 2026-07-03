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
  getCampaignsAction,
  getCampaignByIdAction,
  createCampaignAction,
  updateCampaignAction,
  deleteCampaignAction,
  launchCampaignAction,
  pauseCampaignAction,
  resumeCampaignAction,
  cancelCampaignAction,
  getCampaignStatsAction,
  getCampaignAudienceHealthAction,
  getCampaignRecipientsAction,
  duplicateCampaignAction,
} from "@/features/campaigns/actions/campaign-actions";
import {
  useCreateCampaignDialog,
  useEditCampaignDialog,
  useDeleteCampaignDialog,
  useCancelCampaignDialog,
} from "@/features/campaigns/store/campaign-store";
import type {
  CreateCampaignInput,
  UpdateCampaignInput,
} from "@/features/campaigns/schema/campaign-schema";

export function useCampaignDataTable(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  _sortOrder: string
) {
  return useQuery({
    queryKey: queryKeys.campaigns.list({ page, pageSize, search, status: sortBy }),
    queryFn: async () => {
      const res = await getCampaignsAction({ page, pageSize, search: search || undefined });
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
(useCampaignDataTable as unknown as Record<string, unknown>).isQueryHook = true;

export function useCampaign(id: string | null) {
  return useQuery({
    queryKey: queryKeys.campaigns.detail(id ?? ""),
    queryFn: async () => {
      const res = await getCampaignByIdAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

export function useCampaignStats(id: string | null) {
  return useQuery({
    queryKey: [...queryKeys.campaigns.detail(id ?? ""), "stats"] as const,
    queryFn: async () => {
      const res = await getCampaignStatsAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
    // Poll fast while recipients are still being processed; stop once the queue drains
    // (queued === 0 means every recipient reached a terminal status — nothing left to watch).
    refetchInterval: (query) => {
      const stats = query.state.data;
      if (!stats) return 4_000;
      return stats.queued > 0 ? 4_000 : false;
    },
  });
}

export function useCampaignAudienceHealth(id: string | null) {
  return useQuery({
    queryKey: [...queryKeys.campaigns.detail(id ?? ""), "audience-health"] as const,
    queryFn: async () => {
      const res = await getCampaignAudienceHealthAction(id!);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
  });
}

/**
 * Paged per-recipient delivery list for a campaign. `isLive` (set when the campaign still has
 * queued recipients) drives the same status-aware polling as the stats hook — refetch every ~4s
 * while sending, then stop. keepPreviousData avoids a flash to empty when paging/filtering.
 */
export function useCampaignRecipients(
  id: string | null,
  page: number,
  pageSize: number,
  status: string | undefined,
  isLive: boolean
) {
  return useQuery({
    queryKey: queryKeys.campaigns.recipients(id ?? "", { page, pageSize, status }),
    queryFn: async () => {
      const res = await getCampaignRecipientsAction(id!, page, pageSize, status);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!id,
    placeholderData: keepPreviousData,
    refetchInterval: isLive ? 4_000 : false,
  });
}

export function useCreateCampaign() {
  const qc = useQueryClient();
  const { close } = useCreateCampaignDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateCampaignInput) => {
      const res = await createCampaignAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      setFieldErrors(null);
      close();
      toast.success("Campaign created");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Campaign", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useUpdateCampaign() {
  const qc = useQueryClient();
  const { close } = useEditCampaignDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: UpdateCampaignInput }) => {
      const res = await updateCampaignAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      setFieldErrors(null);
      close();
      toast.success("Campaign updated");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Campaign", "update");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useDeleteCampaign() {
  const qc = useQueryClient();
  const { close } = useDeleteCampaignDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteCampaignAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      close();
      toast.success("Campaign deleted");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Campaign", "delete"),
  });
}

export function useLaunchCampaign() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await launchCampaignAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      toast.success("Campaign launched");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Campaign", "launch"),
  });
}

export function usePauseCampaign() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await pauseCampaignAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      toast.success("Campaign paused");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Campaign", "pause"),
  });
}

export function useResumeCampaign() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await resumeCampaignAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      toast.success("Campaign resumed");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Campaign", "resume"),
  });
}

export function useCancelCampaign() {
  const qc = useQueryClient();
  const { close } = useCancelCampaignDialog();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await cancelCampaignAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      close();
      toast.success("Campaign cancelled");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Campaign", "cancel"),
  });
}

export function useDuplicateCampaign() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await duplicateCampaignAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.campaigns.all });
      toast.success("Campaign duplicated as Draft");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Campaign", "duplicate"),
  });
}
