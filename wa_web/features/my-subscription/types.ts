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

/** A self-service plan as returned by GET /my-subscription/available-plans. */
export interface AvailablePlan {
  id: string;
  name: string;
  monthlyMessageQuota: number;
  price: number;
  currency: string;
  stripePriceId: string | null;
  payHereEnabled: boolean;
}

/** Billing details collected in the PayHere checkout dialog — sent fresh on every checkout. */
export interface PayHereBillingDetails {
  firstName: string;
  lastName: string;
  phone: string;
  address: string;
  city: string;
  country: string;
}

/** The signed payload returned by POST /my-subscription/payhere-checkout, fed straight into payhere.startPayment(). */
export interface PayHereCheckoutPayload {
  merchantId: string;
  orderId: string;
  items: string;
  amount: string;
  currency: string;
  hash: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  address: string;
  city: string;
  country: string;
  notifyUrl: string;
  returnUrl: string;
  cancelUrl: string;
  sandbox: boolean;
}
