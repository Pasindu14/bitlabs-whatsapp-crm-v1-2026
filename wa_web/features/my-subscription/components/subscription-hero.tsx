"use client";

import { AlertCircle } from "lucide-react";
import { DoubleTick } from "@/components/brand-mark";
import { Skeleton } from "@/components/ui/skeleton";
import { useMySubscription } from "@/features/my-subscription/hooks/use-my-subscription";

const num = (n: number) => n.toLocaleString("en-US");
const dateFmt = new Intl.DateTimeFormat("en-US", { day: "numeric", month: "long", year: "numeric" });

/**
 * The subscription page's header: which plan you're on and how much of it is left, as a ring that
 * drains as you send. The one number that matters is in the middle of it.
 */
export function SubscriptionHero() {
  const { data, isLoading, isError } = useMySubscription();

  const quota = data?.hasSubscription ? data.effectiveMessageQuota || data.monthlyMessageQuota : 0;
  const left = data?.hasSubscription ? Math.max(0, data.messagesRemaining) : 0;
  const used = data?.messagesUsedThisPeriod ?? 0;
  const leftShare = quota > 0 ? Math.min(1, left / quota) : 0;
  const ringColor = leftShare <= 0 ? "#ff9f80" : leftShare < 0.1 ? "#ffd27a" : "#dcf8c6";
  const stacked = data?.hasSubscription && data.effectiveMessageQuota > data.monthlyMessageQuota;

  return (
    <section className="relative overflow-hidden rounded-2xl bg-[#0f6b3e] px-6 py-7 text-[#f2faee] sm:px-8 dark:bg-[#0b4f2e]">
      <DoubleTick className="pointer-events-none absolute -bottom-28 left-1/3 w-[24rem] text-[#0b5a33]" />

      <div className="relative flex flex-col gap-8 md:flex-row md:items-center md:justify-between">
        <div className="min-w-0">
          <h1 className="text-[15px] font-medium text-[#b9dcc5]">My subscription</h1>

          {isLoading ? (
            <Skeleton className="mt-3 h-12 w-48 bg-white/15" />
          ) : isError ? (
            <p className="mt-3 flex items-center gap-2 text-[15px]">
              <AlertCircle className="size-4" /> Your plan couldn&apos;t load. Refresh the page to try again.
            </p>
          ) : !data?.hasSubscription ? (
            <>
              <p className="mt-2 text-[2.75rem] leading-none font-light tracking-[-0.03em]">No plan yet</p>
              <p className="mt-3 max-w-md text-[14px] text-[#b9dcc5]">
                Pick a plan below to start sending, or ask your platform administrator to set one up.
              </p>
            </>
          ) : (
            <>
              <p className="mt-2 flex flex-wrap items-center gap-3">
                <span className="text-[3rem] leading-none font-light tracking-[-0.035em]">{data.planName}</span>
                {data.status && (
                  <span className="rounded-full bg-[#dcf8c6] px-2.5 py-0.5 text-[12px] font-medium text-[#0f6b3e]">
                    {data.status}
                  </span>
                )}
              </p>
              <dl className="mt-5 grid grid-cols-2 gap-x-8 gap-y-3 text-[13px] sm:grid-cols-3">
                <Fact label="Allowance" value={`${num(quota)} messages`} />
                <Fact label="Sent this period" value={num(used)} />
                {data.currentPeriodEnd && (
                  <Fact label="Renews" value={dateFmt.format(new Date(data.currentPeriodEnd))} />
                )}
              </dl>
              {stacked && (
                <p className="mt-3 text-[12px] text-[#b9dcc5]">
                  Includes {num(data.effectiveMessageQuota - data.monthlyMessageQuota)} extra messages you bought.
                </p>
              )}
            </>
          )}
        </div>

        {data?.hasSubscription && quota > 0 && (
          <Ring share={leftShare} color={ringColor} left={left} quota={quota} />
        )}
      </div>
    </section>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[#b9dcc5]">{label}</dt>
      <dd className="mt-0.5 text-[15px] font-medium tabular-nums">{value}</dd>
    </div>
  );
}

/** Messages left as a ring. Full ring = whole allowance still available. */
function Ring({ share, color, left, quota }: { share: number; color: string; left: number; quota: number }) {
  const r = 70;
  const c = 2 * Math.PI * r;
  return (
    <div
      className="relative mx-auto size-[11.5rem] shrink-0 md:mx-0"
      role="img"
      aria-label={`${num(left)} of ${num(quota)} messages left`}
    >
      <svg viewBox="0 0 160 160" className="size-full -rotate-90">
        <circle cx="80" cy="80" r={r} fill="none" stroke="rgb(255 255 255 / 0.12)" strokeWidth="10" />
        <circle
          cx="80"
          cy="80"
          r={r}
          fill="none"
          stroke={color}
          strokeWidth="10"
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={c * (1 - share)}
          className="transition-[stroke-dashoffset] duration-700 ease-out motion-reduce:transition-none"
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center text-center">
        <span className="text-[2rem] leading-none font-light tracking-[-0.03em] tabular-nums">{num(left)}</span>
        <span className="mt-1.5 text-[12px] text-[#b9dcc5]">messages left</span>
      </div>
    </div>
  );
}
