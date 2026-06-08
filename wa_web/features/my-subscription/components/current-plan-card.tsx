"use client";

import { CreditCard, Sparkles, AlertCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useMySubscription } from "@/features/my-subscription/hooks/use-my-subscription";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});
const numFmt = new Intl.NumberFormat("en-US");

/**
 * Tenant dashboard hero-card: current plan, message-quota usage, and renewal date.
 * The usage bar shifts from brand-green → amber → red as the quota fills, giving an
 * at-a-glance health signal.
 */
export function CurrentPlanCard() {
  const { data, isLoading, isError } = useMySubscription();

  const pct =
    data?.hasSubscription && data.monthlyMessageQuota > 0
      ? Math.min(100, Math.round((data.messagesUsedThisPeriod / data.monthlyMessageQuota) * 100))
      : 0;

  const barColor =
    pct >= 90
      ? "from-destructive to-destructive/70"
      : pct >= 70
        ? "from-amber-500 to-amber-400"
        : "from-primary to-primary/70";

  return (
    <div className="relative flex flex-col overflow-hidden rounded-xl border bg-card p-6 shadow-sm">
      <div
        aria-hidden
        className="pointer-events-none absolute -right-12 -top-16 h-44 w-44 rounded-full bg-primary/10 blur-3xl"
      />

      {/* Header */}
      <div className="relative flex items-center justify-between">
        <div className="flex items-center gap-2 text-muted-foreground">
          <CreditCard className="h-4 w-4" />
          <p className="text-xs font-medium uppercase tracking-wide">Current Plan</p>
        </div>
        {data?.hasSubscription && data.status && (
          <Badge variant={data.status === "Active" ? "default" : "secondary"}>{data.status}</Badge>
        )}
      </div>

      {isLoading ? (
        <div className="mt-4 space-y-3">
          <Skeleton className="h-8 w-36" />
          <Skeleton className="h-2.5 w-full rounded-full" />
          <Skeleton className="h-3 w-44" />
        </div>
      ) : isError ? (
        <div className="mt-4 flex items-center gap-2 text-sm text-destructive">
          <AlertCircle className="h-4 w-4" />
          Couldn&apos;t load your plan.
        </div>
      ) : !data?.hasSubscription ? (
        <div className="relative mt-4 flex flex-1 flex-col items-start justify-center gap-1 py-2">
          <div className="mb-1 flex h-10 w-10 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            <Sparkles className="h-5 w-5" />
          </div>
          <p className="text-lg font-semibold">No active plan</p>
          <p className="text-sm text-muted-foreground">
            Contact your platform administrator to get set up.
          </p>
        </div>
      ) : (
        <div className="relative mt-4 flex flex-1 flex-col gap-4">
          <div className="flex items-end justify-between gap-4">
            <div>
              <p className="text-3xl font-bold tracking-tight">{data.planName}</p>
              <p className="mt-0.5 text-xs text-muted-foreground">
                {numFmt.format(data.monthlyMessageQuota)} messages / period
              </p>
            </div>
            <div className="text-right">
              <p className="text-2xl font-semibold tabular-nums text-primary">
                {numFmt.format(data.messagesRemaining)}
              </p>
              <p className="text-xs text-muted-foreground">remaining</p>
            </div>
          </div>

          {data.monthlyMessageQuota > 0 && (
            <div className="space-y-1.5">
              <div className="h-2.5 w-full overflow-hidden rounded-full bg-muted">
                <div
                  className={cn(
                    "h-full rounded-full bg-gradient-to-r transition-[width] duration-700 ease-out",
                    barColor,
                  )}
                  style={{ width: `${Math.max(pct, 2)}%` }}
                />
              </div>
              <div className="flex items-center justify-between text-xs text-muted-foreground tabular-nums">
                <span>
                  {numFmt.format(data.messagesUsedThisPeriod)} / {numFmt.format(data.monthlyMessageQuota)} used
                </span>
                <span>{pct}%</span>
              </div>
            </div>
          )}

          {data.currentPeriodEnd && (
            <p className="mt-auto border-t pt-3 text-xs text-muted-foreground">
              Renews {dateFmt.format(new Date(data.currentPeriodEnd))}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
