"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/hooks/query-keys";
import {
  getMySubscriptionAction,
  getInvoicesAction,
  getSubscriptionHistoryAction,
  getAvailablePlansAction,
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

/** CompanyAdmin — plans that have a StripePriceId set (self-service checkout). */
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
