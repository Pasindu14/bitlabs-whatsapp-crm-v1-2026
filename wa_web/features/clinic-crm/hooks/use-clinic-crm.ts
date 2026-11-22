"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  deleteClinicCrmAction,
  getClinicCrmAction,
  getClinicCrmTreatmentsAction,
  saveClinicCrmAction,
  saveClinicCrmMappingsAction,
  testClinicCrmAction,
} from "@/features/clinic-crm/actions/clinic-crm-actions";
import type {
  SaveClinicCrmConnectionInput,
  SaveClinicCrmMappingsInput,
} from "@/features/clinic-crm/schema/clinic-crm-schema";

export function useClinicCrm() {
  return useQuery({
    queryKey: queryKeys.clinicCrm.settings(),
    queryFn: async () => {
      const res = await getClinicCrmAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

/**
 * The clinic's live treatment list, for the mapping dropdowns. Only fetched once a connection is
 * saved — before that there is nothing to ask — and not retried, because a failure here is a
 * connection problem the admin needs to see rather than wait out.
 */
export function useClinicCrmTreatments(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.clinicCrm.treatments(),
    enabled,
    retry: false,
    queryFn: async () => {
      const res = await getClinicCrmTreatmentsAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useSaveClinicCrm() {
  const qc = useQueryClient();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: SaveClinicCrmConnectionInput) => {
      const res = await saveClinicCrmAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.clinicCrm.all });
      setFieldErrors(null);
      toast.success("Clinic CRM connection saved");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Clinic CRM connection", "save");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useDeleteClinicCrm() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const res = await deleteClinicCrmAction();
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.clinicCrm.all });
      toast.success("Clinic CRM disconnected");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Clinic CRM connection", "remove"),
  });
}

/** "Test connection": a token and the treatment list. Never creates anything in the CRM. */
export function useTestClinicCrm() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const res = await testClinicCrmAction();
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: queryKeys.clinicCrm.all });
      if (result.ok) toast.success(result.message);
      else toast.error(result.message);
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Clinic CRM connection", "test"),
  });
}

export function useSaveClinicCrmMappings() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (data: SaveClinicCrmMappingsInput) => {
      const res = await saveClinicCrmMappingsAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.clinicCrm.settings() });
      toast.success("Treatments matched");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Treatment matching", "save"),
  });
}
