/** Re-export the shared Subscription/SubscriptionPurchase shapes — the tenant reads return the same DTOs. */
export type {
  Subscription,
  SubscriptionStatus,
  SubscriptionPurchase,
  SubscriptionPurchaseMode,
} from "@/features/subscriptions/types";

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
