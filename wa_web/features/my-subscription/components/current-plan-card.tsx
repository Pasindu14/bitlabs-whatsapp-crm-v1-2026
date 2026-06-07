"use client";

import { CreditCard } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";
import { useMySubscription } from "@/features/my-subscription/hooks/use-my-subscription";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});
const numFmt = new Intl.NumberFormat("en-US");

/** Tenant dashboard card: current plan, message quota usage, and period end. */
export function CurrentPlanCard() {
  const { data, isLoading, isError } = useMySubscription();

  return (
    <div className="rounded-xl border bg-card p-5 shadow-sm">
      <div className="flex items-center gap-2 text-muted-foreground">
        <CreditCard className="h-4 w-4" />
        <p className="text-xs font-medium uppercase tracking-wide">Current Plan</p>
      </div>

      {isLoading ? (
        <div className="mt-3 space-y-2">
          <Skeleton className="h-6 w-32" />
          <Skeleton className="h-2 w-full" />
          <Skeleton className="h-3 w-40" />
        </div>
      ) : isError ? (
        <p className="mt-2 text-sm text-destructive">Couldn&apos;t load your plan.</p>
      ) : !data?.hasSubscription ? (
        <div className="mt-2">
          <p className="text-lg font-semibold">No active plan</p>
          <p className="text-sm text-muted-foreground">
            Contact your platform administrator to get set up.
          </p>
        </div>
      ) : (
        <div className="mt-2 space-y-3">
          <div className="flex items-center gap-2">
            <p className="text-lg font-semibold">{data.planName}</p>
            {data.status && (
              <Badge variant={data.status === "Active" ? "default" : "secondary"}>
                {data.status}
              </Badge>
            )}
          </div>

          {data.monthlyMessageQuota > 0 && (
            <div className="space-y-1">
              <Progress
                value={Math.min(
                  100,
                  Math.round((data.messagesUsedThisPeriod / data.monthlyMessageQuota) * 100)
                )}
                className="h-2"
              />
              <p className="text-xs text-muted-foreground">
                {numFmt.format(data.messagesUsedThisPeriod)} /{" "}
                {numFmt.format(data.monthlyMessageQuota)} messages used
                {" · "}
                {numFmt.format(data.messagesRemaining)} left
              </p>
            </div>
          )}

          {data.currentPeriodEnd && (
            <p className="text-xs text-muted-foreground">
              Renews {dateFmt.format(new Date(data.currentPeriodEnd))}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
