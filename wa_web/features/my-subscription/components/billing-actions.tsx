"use client";

import { useState } from "react";
import Script from "next/script";
import { ExternalLink, Zap, Settings2, Loader2, CreditCard } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useAvailablePlans, useCreatePayHereCheckout, useRefreshAfterPayHere } from "@/features/my-subscription/hooks/use-my-subscription";
import {
  createCheckoutSessionAction,
  createPortalSessionAction,
} from "@/features/my-subscription/actions/my-subscription-actions";
import { PayHereBillingDetailsDialog } from "@/features/my-subscription/components/payhere-billing-details-dialog";
import { useUsdToAedRate } from "@/features/fx/hooks/use-fx";
import { formatDirhamFirst } from "@/features/fx/format";
import type { AvailablePlan, PayHereCheckoutPayload } from "@/features/my-subscription/types";
import type { PayHereBillingInput } from "@/features/my-subscription/schema/payhere-checkout-schema";

const currFmt = (amount: number, currency: string) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);
const numFmt = new Intl.NumberFormat("en-US");

const PAYHERE_SCRIPT_SRC = "https://www.payhere.lk/lib/payhere.js";

// PayHere's onsite JS expects snake_case fields, distinct from our camelCase API payload.
declare global {
  interface Window {
    payhere?: {
      startPayment: (payload: Record<string, unknown>) => void;
      onCompleted?: (orderId: string) => void;
      onDismissed?: () => void;
      onError?: (error: string) => void;
    };
  }
}

function toPayHerePayload(payload: PayHereCheckoutPayload) {
  return {
    sandbox: payload.sandbox,
    merchant_id: payload.merchantId,
    return_url: payload.returnUrl,
    cancel_url: payload.cancelUrl,
    notify_url: payload.notifyUrl,
    order_id: payload.orderId,
    items: payload.items,
    amount: payload.amount,
    currency: payload.currency,
    hash: payload.hash,
    first_name: payload.firstName,
    last_name: payload.lastName,
    email: payload.email,
    phone: payload.phone,
    address: payload.address,
    city: payload.city,
    country: payload.country,
  };
}

/**
 * CompanyAdmin-only billing controls.
 * Renders available plans (Stripe checkout and/or PayHere onsite popup) and a "Manage Billing" portal link.
 */
export function BillingActionsCard() {
  const { data: plans, isLoading, isError } = useAvailablePlans();
  const { data: fxRate } = useUsdToAedRate();
  const [loadingPlanId, setLoadingPlanId] = useState<string | null>(null);
  const [portalLoading, setPortalLoading] = useState(false);
  const [payHerePlan, setPayHerePlan] = useState<AvailablePlan | null>(null);
  const [payHereReady, setPayHereReady] = useState(false);

  const createPayHereCheckout = useCreatePayHereCheckout();
  const refreshAfterPayHere = useRefreshAfterPayHere();

  async function handleStripeCheckout(planId: string) {
    setLoadingPlanId(planId);
    try {
      const res = await createCheckoutSessionAction(planId);
      if (!res.success) {
        toast.error(res.error ?? "Could not start checkout.");
        return;
      }
      window.location.href = res.data;
    } catch {
      toast.error("Unexpected error starting checkout.");
    } finally {
      setLoadingPlanId(null);
    }
  }

  async function handlePortal() {
    setPortalLoading(true);
    try {
      const res = await createPortalSessionAction();
      if (!res.success) {
        toast.error(res.error ?? "Could not open billing portal.");
        return;
      }
      window.open(res.data, "_blank", "noopener,noreferrer");
    } catch {
      toast.error("Unexpected error opening billing portal.");
    } finally {
      setPortalLoading(false);
    }
  }

  async function handlePayHereConfirm(billing: PayHereBillingInput) {
    if (!payHerePlan) return;

    if (!payHereReady || !window.payhere) {
      toast.error("Payment provider is still loading — try again in a moment.");
      return;
    }

    try {
      const payload = await createPayHereCheckout.mutateAsync({ planId: payHerePlan.id, billing });

      window.payhere.onCompleted = () => {
        toast.success("Payment successful — your plan will update shortly.");
        refreshAfterPayHere();
      };
      window.payhere.onDismissed = () => {
        toast.info("Checkout dismissed.");
      };
      window.payhere.onError = (error: string) => {
        toast.error(`Payment error: ${error}`);
      };

      setPayHerePlan(null);
      window.payhere.startPayment(toPayHerePayload(payload));
    } catch {
      // createPayHereCheckout's onError already toasts the specific failure.
    }
  }

  const anyLoading = loadingPlanId !== null;

  return (
    <div className="flex flex-col gap-4 rounded-xl border bg-card p-6 shadow-sm">
      <Script src={PAYHERE_SCRIPT_SRC} strategy="afterInteractive" onLoad={() => setPayHereReady(true)} />

      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Zap className="h-4 w-4 text-muted-foreground" />
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Plans &amp; Billing
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={handlePortal}
          disabled={portalLoading}
        >
          {portalLoading ? (
            <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
          ) : (
            <Settings2 className="mr-1.5 h-3.5 w-3.5" />
          )}
          Manage Billing
        </Button>
      </div>

      {isLoading ? (
        <div className="grid gap-3 sm:grid-cols-2">
          {[1, 2].map((i) => (
            <Skeleton key={i} className="h-24 w-full rounded-lg" />
          ))}
        </div>
      ) : isError ? (
        <p className="text-sm text-destructive">Could not load available plans.</p>
      ) : !plans?.length ? (
        <p className="text-sm text-muted-foreground">
          No self-service plans are configured yet. Contact your platform administrator.
        </p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2">
          {plans.map((plan) => {
            const dirhamFirst = formatDirhamFirst(plan.price, plan.currency, fxRate?.rate);
            return (
            <div
              key={plan.id}
              className="flex flex-col gap-3 rounded-lg border bg-muted/30 p-4"
            >
              <div>
                <p className="font-semibold">{plan.name}</p>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {numFmt.format(plan.monthlyMessageQuota)} messages / month
                </p>
              </div>
              <div className="flex items-end justify-between gap-2">
                <div>
                  {/* Dirhams lead for the UAE audience, but the USD line below is the real charge. */}
                  <p className="text-lg font-bold">
                    {dirhamFirst ? dirhamFirst.aed : currFmt(plan.price, plan.currency)}
                    <span className="text-xs font-normal text-muted-foreground"> /mo</span>
                  </p>
                  {dirhamFirst && (
                    <p className="text-xs text-muted-foreground">
                      {dirhamFirst.usd} charged in USD
                    </p>
                  )}
                </div>
                <div className="flex flex-col gap-1.5">
                  {plan.stripePriceId && (
                    <Button
                      size="sm"
                      onClick={() => handleStripeCheckout(plan.id)}
                      disabled={anyLoading}
                    >
                      {loadingPlanId === plan.id ? (
                        <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                      ) : (
                        <ExternalLink className="mr-1.5 h-3.5 w-3.5" />
                      )}
                      Subscribe
                    </Button>
                  )}
                  {plan.payHereEnabled && (
                    <Button
                      size="sm"
                      variant={plan.stripePriceId ? "outline" : "default"}
                      onClick={() => setPayHerePlan(plan)}
                      disabled={anyLoading}
                    >
                      <CreditCard className="mr-1.5 h-3.5 w-3.5" />
                      Pay with PayHere
                    </Button>
                  )}
                </div>
              </div>
            </div>
            );
          })}
        </div>
      )}

      {/* Disclosure: dirhams are the headline but PayHere settles in USD only, so say so plainly. */}
      {fxRate && plans?.some((p) => p.currency?.toUpperCase() === "USD") && (
        <p className="text-xs text-muted-foreground">
          Dirham prices are indicative, converted at {fxRate.rate} AED per USD. Your card is charged
          the USD amount shown; your bank may apply its own conversion or foreign-transaction fee.
        </p>
      )}

      <PayHereBillingDetailsDialog
        open={payHerePlan !== null}
        onOpenChange={(open) => !open && setPayHerePlan(null)}
        planName={payHerePlan?.name ?? ""}
        onConfirm={handlePayHereConfirm}
        isLoading={createPayHereCheckout.isPending}
      />
    </div>
  );
}
