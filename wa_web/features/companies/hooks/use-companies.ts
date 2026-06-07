"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  createCompanyAction,
  deleteCompanyAction,
} from "@/features/companies/actions/company-actions";
import type { CreateCompanyInput } from "@/features/companies/schema/company-schema";

/** Create a company, then invalidate the list so the table refetches. */
export function useCreateCompany(onSuccess?: () => void) {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (input: CreateCompanyInput) => {
      const res = await createCompanyAction(input);
      if (!res.success) throw res; // ActionFailure
      return res.data;
    },
    onSuccess: () => {
      toast.success("Company created");
      qc.invalidateQueries({ queryKey: queryKeys.companies.all });
      onSuccess?.();
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "company", "create"),
  });
}

/** Soft-delete (deactivate) a company. */
export function useDeleteCompany(onSuccess?: () => void) {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteCompanyAction(id);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      toast.success("Company deactivated");
      qc.invalidateQueries({ queryKey: queryKeys.companies.all });
      onSuccess?.();
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "company", "delete"),
  });
}
