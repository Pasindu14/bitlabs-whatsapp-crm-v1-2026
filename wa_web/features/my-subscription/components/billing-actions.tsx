"use client";

import { useState } from "react";
import Script from "next/script";
import { ExternalLink, Settings2, Loader2, CreditCard } from "lucide-react";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useAvailablePlans, useCreatePayHereCheckout, useMySubscription, useRefreshAfterPayHere } from "@/features/my-subscription/hooks/use-my-subscription";
import {
  createCheckoutSessionAction,
  createPortalSessionAction,
} from "@/features/my-subscription/actions/my-subscription-actions";
import { useUsdToAedRate } from "@/features/fx/hooks/use-fx";
import { formatDirhamFirst, formatUsdApprox } from "@/features/fx/format";
import type { AvailablePlan, PayHereCheckoutPayload } from "@/features/my-subscription/types";

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
  const { data: current } = useMySubscription();
  const [loadingPlanId, setLoadingPlanId] = useState<string | null>(null);
  const [portalLoading, setPortalLoading] = useState(false);
  const [payHereLoadingId, setPayHereLoadingId] = useState<string | null>(null);
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

  async function handlePayHereCheckout(plan: AvailablePlan) {
    if (!payHereReady || !window.payhere) {
      toast.error("Payment provider is still loading — try again in a moment.");
      return;
    }

    setPayHereLoadingId(plan.id);
    try {
      const payload = await createPayHereCheckout.mutateAsync({ planId: plan.id });

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

      window.payhere.startPayment(toPayHerePayload(payload));
    } catch {
      // createPayHereCheckout's onError already toasts the specific failure.
    } finally {
      setPayHereLoadingId(null);
    }
  }

  const anyLoading = loadingPlanId !== null || payHereLoadingId !== null;

  return (
    <Card className="rounded-xl">
      <Script src={PAYHERE_SCRIPT_SRC} strategy="afterInteractive" onLoad={() => setPayHereReady(true)} />

      <CardHeader>
        <CardTitle className="text-[15px]">Plans and billing</CardTitle>
        <CardDescription>Move to a bigger plan, or top up when you&apos;re running low.</CardDescription>
        <CardAction>
          <Button variant="outline" size="sm" onClick={handlePortal} disabled={portalLoading} className="rounded-full">
            {portalLoading ? <Loader2 className="animate-spin" data-icon="inline-start" /> : <Settings2 data-icon="inline-start" />}
            Manage billing
          </Button>
        </CardAction>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        {isLoading ? (
          <div className="grid gap-3 sm:grid-cols-2">
            {[1, 2].map((i) => (
              <Skeleton key={i} className="h-36 w-full rounded-xl" />
            ))}
          </div>
        ) : isError ? (
          <p className="text-sm text-destructive">Plans couldn&apos;t load. Refresh the page to try again.</p>
        ) : !plans?.length ? (
          <p className="rounded-xl border border-dashed px-4 py-8 text-center text-sm text-muted-foreground">
            There are no plans to buy here yet. Ask your platform administrator to add one.
          </p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            {plans.map((plan) => {
              const dirhamFirst = formatDirhamFirst(plan.price, plan.currency, fxRate?.rate);
              const usdApprox = !dirhamFirst ? formatUsdApprox(plan.price, plan.currency, fxRate?.rate) : null;
              const isCurrent = current?.hasSubscription && current.planName === plan.name;
              return (
                <div
                  key={plan.id}
                  className={cn(
                    "relative flex flex-col gap-4 rounded-xl border p-5 transition-colors",
                    isCurrent ? "border-primary/50 bg-[#f1f8f3] dark:bg-primary/10" : "bg-card hover:border-primary/30",
                  )}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="text-[16px] font-medium">{plan.name}</p>
                      <p className="mt-0.5 text-[13px] text-muted-foreground">
                        {numFmt.format(plan.monthlyMessageQuota)} messages a month
                      </p>
                    </div>
                    {isCurrent && (
                      <span className="rounded-full bg-primary px-2 py-0.5 text-[11px] font-medium text-primary-foreground">
                        Your plan
                      </span>
                    )}
                  </div>

                  <div>
                    {/* Dirhams lead for the UAE audience, but the USD line below is the real charge. */}
                    <p className="text-[1.75rem] leading-none font-light tracking-[-0.02em] tabular-nums">
                      {dirhamFirst ? dirhamFirst.aed : currFmt(plan.price, plan.currency)}
                      <span className="ml-1 text-[13px] text-muted-foreground">a month</span>
                    </p>
                    {dirhamFirst ? (
                      <p className="mt-1 text-[12px] text-muted-foreground">{dirhamFirst.usd} charged in USD</p>
                    ) : (
                      usdApprox && <p className="mt-1 text-[12px] text-muted-foreground">{usdApprox}</p>
                    )}
                  </div>

                  <div className="mt-auto flex flex-wrap gap-2">
                    {plan.stripePriceId && (
                      <Button size="sm" onClick={() => handleStripeCheckout(plan.id)} disabled={anyLoading} className="rounded-full">
                        {loadingPlanId === plan.id ? (
                          <Loader2 className="animate-spin" data-icon="inline-start" />
                        ) : (
                          <ExternalLink data-icon="inline-start" />
                        )}
                        Subscribe with card
                      </Button>
                    )}
                    {plan.payHereEnabled && (
                      <Button
                        size="sm"
                        variant={plan.stripePriceId ? "outline" : "default"}
                        onClick={() => handlePayHereCheckout(plan)}
                        disabled={anyLoading}
                        className="rounded-full"
                      >
                        {payHereLoadingId === plan.id ? (
                          <Loader2 className="animate-spin" data-icon="inline-start" />
                        ) : (
                          <CreditCard data-icon="inline-start" />
                        )}
                        Pay with PayHere
                      </Button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* Disclosure: whichever currency isn't the plan's native one is an indicative conversion,
            not the charge — say so plainly for both directions. */}
        {fxRate && plans?.some((p) => ["USD", "AED"].includes(p.currency?.toUpperCase())) && (
          <p className="text-[12px] leading-relaxed text-muted-foreground">
            Prices in a second currency are a guide, converted at {fxRate.rate} AED per USD. You pay in the
            plan&apos;s own currency, and your bank may add its own conversion fee.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
