/** Subscription status — mirrors wa_api SubscriptionStatus enum. */
export type SubscriptionStatus = "Active" | "Inactive" | "Trialing" | "Cancelled";

/** A subscription as returned by wa_api (mirrors SubscriptionResponse). */
export interface Subscription {
  id: string | null;
  companyId: string;
  companyName: string | null;
  planId: string | null;
  planName: string | null;
  status: SubscriptionStatus | null;
  monthlyMessageQuota: number;
  extraMessageCredits: number;
  effectiveMessageQuota: number;
  messagesUsedThisPeriod: number;
  messagesRemaining: number;
  currentPeriodStart: string | null;
  currentPeriodEnd: string | null;
  isActive: boolean;
  hasSubscription: boolean;
  createdAt: string | null;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined;
}

/** A package-purchase audit row (mirrors wa_api PackagePurchaseResponse). */
export interface PackagePurchase {
  id: string;
  companyId: string;
  subscriptionId: string;
  packageId: string;
  packageName: string;
  messagesAdded: number;
  price: number;
  currency: string;
  createdAt: string;
}

/** How a subscribe was applied — mirrors wa_api SubscriptionPurchaseMode. */
export type SubscriptionPurchaseMode = "Fresh" | "Stack";

/** A subscribe (plan-assignment) audit row (mirrors wa_api SubscriptionPurchaseResponse). */
export interface SubscriptionPurchase {
  id: string;
  companyId: string;
  subscriptionId: string;
  planId: string;
  planName: string;
  mode: SubscriptionPurchaseMode;
  messagesAdded: number;
  periodDays: number;
  balanceAfter: number;
  periodEndAfter: string;
  price: number;
  currency: string;
  createdAt: string;
}

/** Filters/pagination the list endpoint accepts. */
export interface SubscriptionListParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
}
