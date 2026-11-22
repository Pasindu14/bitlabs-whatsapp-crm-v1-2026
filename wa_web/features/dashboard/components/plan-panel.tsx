"use client";

import Link from "next/link";
import { CalendarClock, Contact, AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle, CardDescription, CardAction } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useMySubscription } from "@/features/my-subscription/hooks/use-my-subscription";

const num = (n: number) => n.toLocaleString("en-US");
const dateFmt = new Intl.DateTimeFormat("en-US", { day: "numeric", month: "short", year: "numeric" });

/** Plan allowance, renewal and audience size in one place. */
export function PlanPanel({ contacts }: { contacts: number | null }) {
  const { data, isLoading, isError } = useMySubscription();

  const quota = data?.hasSubscription ? data.effectiveMessageQuota || data.monthlyMessageQuota : 0;
  const used = data?.messagesUsedThisPeriod ?? 0;
  const usedPct = quota > 0 ? Math.min(100, Math.round((used / quota) * 100)) : 0;
  const tight = usedPct >= 90 ? "text-destructive" : usedPct >= 70 ? "text-amber-600" : "text-primary";

  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle className="text-[15px]">Your plan</CardTitle>
        <CardDescription>Messages you can still send this period.</CardDescription>
        <CardAction>
          <Button asChild variant="ghost" size="sm">
            <Link href="/my-subscription">Manage</Link>
          </Button>
        </CardAction>
      </CardHeader>

      <CardContent className="flex flex-1 flex-col gap-5">
        {isLoading ? (
          <div className="space-y-3">
            <Skeleton className="h-10 w-40" />
            <Skeleton className="h-2 w-full" />
          </div>
        ) : isError ? (
          <p className="flex items-center gap-2 text-sm text-destructive">
            <AlertCircle className="size-4" /> Your plan couldn&apos;t load. Refresh to try again.
          </p>
        ) : !data?.hasSubscription ? (
          <p className="text-sm text-muted-foreground">
            You don&apos;t have an active plan. Ask your platform administrator to set one up.
          </p>
        ) : (
          <>
            <div>
              <p className={cn("text-[2.5rem] leading-none font-light tracking-[-0.03em] tabular-nums", tight)}>
                {num(data.messagesRemaining)}
              </p>
              <p className="mt-1.5 text-[13px] text-muted-foreground">
                left of {num(quota)} on the <span className="font-medium text-foreground">{data.planName}</span> plan
              </p>
            </div>
            <div className="space-y-1.5">
              <Progress value={Math.max(usedPct, 1)} className="h-2" />
              <p className="flex justify-between text-[12px] text-muted-foreground tabular-nums">
                <span>{num(used)} used</span>
                <span>{usedPct}%</span>
              </p>
            </div>
          </>
        )}

        <div className="mt-auto grid grid-cols-2 gap-3 border-t pt-4 text-[13px]">
          <div className="flex items-start gap-2.5">
            <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-[#dcebfa] text-[#1b5e9e]">
              <Contact className="size-4" />
            </span>
            <div className="leading-tight">
              <p className="text-[17px] font-medium tabular-nums">{contacts === null ? "–" : num(contacts)}</p>
              <Link href="/contacts" className="text-[12px] text-muted-foreground hover:text-foreground hover:underline">
                Contacts
              </Link>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-[#fbe7d3] text-[#9a4f0f]">
              <CalendarClock className="size-4" />
            </span>
            <div className="leading-tight">
              <p className="text-[15px] font-medium">
                {data?.currentPeriodEnd ? dateFmt.format(new Date(data.currentPeriodEnd)) : "–"}
              </p>
              <p className="text-[12px] text-muted-foreground">Renews</p>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
