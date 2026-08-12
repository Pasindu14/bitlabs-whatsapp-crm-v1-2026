"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  getMySubscriptionAction,
  getInvoicesAction,
  getSubscriptionHistoryAction,
  getAvailablePlansAction,
  createPayHereCheckoutAction,
} from "@/features/my-subscription/actions/my-subscription-actions";

/** The caller's own active subscription + usage. `hasSubscription` is false when none. */
export function useMySubscription() {
  return useQuery({
    queryKey: queryKeys.mySubscription.current(),
    queryFn: async () => {
      const res = await getMySubscriptionAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 30_000,
  });
}

/** CompanyAdmin — invoice history from Stripe. */
export function useInvoices() {
  return useQuery({
    queryKey: queryKeys.mySubscription.invoices(),
    queryFn: async () => {
      const res = await getInvoicesAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 60_000,
  });
}

/** CompanyAdmin — every SuperAdmin plan assignment/change for the caller's company (manual counterpart to Stripe invoices). */
export function useSubscriptionHistory() {
  return useQuery({
    queryKey: queryKeys.mySubscription.subscriptionHistory(),
    queryFn: async () => {
      const res = await getSubscriptionHistoryAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 60_000,
  });
}

/** CompanyAdmin — plans purchasable via self-service checkout (Stripe and/or PayHere). */
export function useAvailablePlans() {
  return useQuery({
    queryKey: queryKeys.mySubscription.availablePlans(),
    queryFn: async () => {
      const res = await getAvailablePlansAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    staleTime: 5 * 60_000,
  });
}

/**
 * CompanyAdmin — creates a Pending PayHereOrder and returns the signed payload for
 * payhere.startPayment(). The subscription itself is applied later by the notify webhook, so
 * success here only invalidates current/history once the caller's onCompleted callback fires.
 */
export function useCreatePayHereCheckout() {
  return useMutation({
    mutationFn: async ({ planId }: { planId: string }) => {
      const res = await createPayHereCheckoutAction(planId);
      if (!res.success) throw res;
      return res.data;
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Checkout", "create"),
  });
}

/** Invalidates subscription state after a PayHere payment completes (call from onCompleted). */
export function useRefreshAfterPayHere() {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: queryKeys.mySubscription.current() });
    qc.invalidateQueries({ queryKey: queryKeys.mySubscription.subscriptionHistory() });
  };
}
