"use client";

import { useState } from "react";
import { ExternalLink, Zap, Settings2, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useAvailablePlans } from "@/features/my-subscription/hooks/use-my-subscription";
import {
  createCheckoutSessionAction,
  createPortalSessionAction,
} from "@/features/my-subscription/actions/my-subscription-actions";

const currFmt = (amount: number, currency: string) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);
const numFmt = new Intl.NumberFormat("en-US");

/**
 * CompanyAdmin-only billing controls.
 * Renders available plans (Stripe checkout) and a "Manage Billing" portal link.
 */
export function BillingActionsCard() {
  const { data: plans, isLoading, isError } = useAvailablePlans();
  const [loadingPlanId, setLoadingPlanId] = useState<string | null>(null);
  const [portalLoading, setPortalLoading] = useState(false);

  async function handleCheckout(planId: string) {
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

  return (
    <div className="flex flex-col gap-4 rounded-xl border bg-card p-6 shadow-sm">
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
          {plans.map((plan) => (
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
              <div className="flex items-end justify-between">
                <p className="text-lg font-bold">
                  {currFmt(plan.price, plan.currency)}
                  <span className="text-xs font-normal text-muted-foreground"> /mo</span>
                </p>
                <Button
                  size="sm"
                  onClick={() => handleCheckout(plan.id)}
                  disabled={loadingPlanId !== null}
                >
                  {loadingPlanId === plan.id ? (
                    <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <ExternalLink className="mr-1.5 h-3.5 w-3.5" />
                  )}
                  Subscribe
                </Button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
