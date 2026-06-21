/** Re-export the shared Subscription shape — the tenant read returns the same DTO. */
export type { Subscription, SubscriptionStatus } from "@/features/subscriptions/types";

/** A Stripe invoice as returned by GET /my-subscription/invoices. */
export interface Invoice {
  id: string;
  stripeInvoiceId: string;
  amountPaid: number;
  currency: string;
  status: string;
  paidAt: string | null;
  hostedInvoiceUrl: string | null;
  invoicePdfUrl: string | null;
  createdAt: string;
}

/** A self-service plan (has StripePriceId) as returned by GET /my-subscription/available-plans. */
export interface AvailablePlan {
  id: string;
  name: string;
  monthlyMessageQuota: number;
  price: number;
  currency: string;
  stripePriceId: string | null;
}
