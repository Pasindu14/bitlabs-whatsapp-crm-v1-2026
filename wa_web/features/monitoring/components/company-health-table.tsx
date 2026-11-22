"use client";

import { AlertTriangle, CheckCircle2, Megaphone, XCircle } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useCompanyHealth } from "@/features/monitoring/hooks/use-monitoring";
import type { CompanyHealth } from "@/features/monitoring/types";

const numFmt = new Intl.NumberFormat("en-US");
const dateFmt = new Intl.DateTimeFormat("en-US", { day: "numeric", month: "short" });

/** A company is flagged above this failure rate (mirrors the API's rule). */
const FLAG_AT = 0.1;

const QUALITY: Record<string, { label: string; className: string }> = {
  GREEN: { label: "High quality", className: "bg-[#e3f1e8] text-[#0f6b3e]" },
  YELLOW: { label: "Medium quality", className: "bg-amber-50 text-amber-700" },
  RED: { label: "Low quality", className: "bg-red-50 text-red-700" },
};

function StatusPill({ company }: { company: CompanyHealth }) {
  if (!company.isActive)
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-[12px] text-muted-foreground">
        <XCircle className="size-3.5" /> Inactive
      </span>
    );
  if (company.isFlagged)
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-[12px] font-medium text-amber-700">
        <AlertTriangle className="size-3.5" /> Flagged
      </span>
    );
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-[#e3f1e8] px-2 py-0.5 text-[12px] font-medium text-[#0f6b3e]">
      <CheckCircle2 className="size-3.5" /> Healthy
    </span>
  );
}

/**
 * Failure rate on a scale that always shows the 10% flag line, so "how far over" is visible at a
 * glance — a bare percentage can't say whether 12% is a near miss or a fire.
 */
function FailureBar({ rate, sent }: { rate: number; sent: number }) {
  if (sent === 0) return <p className="text-[12.5px] text-muted-foreground">Nothing sent in 30 days</p>;
  const scaleMax = Math.max(0.4, Math.ceil((rate + 0.05) * 10) / 10);
  const fill = rate >= 0.25 ? "bg-[#e5533d]" : rate >= FLAG_AT ? "bg-amber-400" : "bg-[#1fbf5c]";
  return (
    <div className="w-full">
      <div className="flex items-baseline justify-between text-[12.5px]">
        <span className="text-muted-foreground">Failed</span>
        <span className={cn("font-medium tabular-nums", rate >= FLAG_AT ? "text-amber-700" : "text-foreground")}>
          {(rate * 100).toFixed(1)}%
        </span>
      </div>
      <div
        className="relative mt-1.5 h-2 w-full rounded-full bg-muted"
        role="img"
        aria-label={`${(rate * 100).toFixed(1)}% of messages failed; flagged above ${FLAG_AT * 100}%`}
      >
        <div className={cn("h-full rounded-full", fill)} style={{ width: `${Math.min(100, (rate / scaleMax) * 100)}%` }} />
        {/* The flag line. */}
        <span
          className="absolute -top-1 -bottom-1 w-0.5 rounded-full bg-foreground/60"
          style={{ left: `${(FLAG_AT / scaleMax) * 100}%` }}
          title="Flagged above 10%"
        />
      </div>
    </div>
  );
}

function CompanyRow({ company }: { company: CompanyHealth }) {
  const quality = company.qualityRating ? QUALITY[company.qualityRating.toUpperCase()] : undefined;
  return (
    <li
      className={cn(
        "grid items-center gap-x-6 gap-y-3 px-5 py-4 md:grid-cols-[minmax(0,1.2fr)_minmax(0,1.4fr)_minmax(0,1fr)]",
        !company.isActive && "opacity-60",
      )}
    >
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <p className="truncate text-[15px] font-medium">{company.name}</p>
          <StatusPill company={company} />
        </div>
        <p className="mt-1 text-[12.5px] text-muted-foreground tabular-nums">
          {numFmt.format(company.totalSent30d)} sent, {numFmt.format(company.failed30d)} failed
        </p>
      </div>

      <FailureBar rate={company.failureRate} sent={company.totalSent30d} />

      <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[12.5px] md:justify-end">
        {quality ? (
          <span className={cn("rounded-full px-2 py-0.5 font-medium", quality.className)}>{quality.label}</span>
        ) : (
          <span className="text-muted-foreground">Quality not rated</span>
        )}
        {company.activeCampaigns > 0 && (
          <span className="inline-flex items-center gap-1 text-[#1b5e9e]">
            <Megaphone className="size-3.5" /> {company.activeCampaigns} sending
          </span>
        )}
        <span className="text-muted-foreground">
          {company.lastMessageAt ? `Last message ${dateFmt.format(new Date(company.lastMessageAt))}` : "Never messaged"}
        </span>
      </div>
    </li>
  );
}

export function CompanyHealthTable() {
  const { data, isLoading, isError } = useCompanyHealth();

  if (isLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-20 w-full rounded-xl" />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <p className="rounded-xl border bg-card px-4 py-10 text-center text-sm text-muted-foreground">
        Company health couldn&apos;t load. Refresh the page to try again.
      </p>
    );
  }

  // Flagged active companies first, then healthy ones by volume, inactive last.
  const sorted = [...(data?.companies ?? [])].sort((a, b) => {
    const score = (c: CompanyHealth) => (c.isActive ? (c.isFlagged ? 2 : 1) : 0);
    return score(b) - score(a) || b.totalSent30d - a.totalSent30d;
  });

  if (sorted.length === 0) {
    return (
      <p className="rounded-xl border border-dashed px-4 py-10 text-center text-sm text-muted-foreground">
        No companies yet. Add one under Companies and its numbers will show here.
      </p>
    );
  }

  return (
    <section className="overflow-hidden rounded-2xl border bg-card shadow-[0_14px_34px_-26px_rgb(16_60_36/0.5)]">
      <header className="flex flex-wrap items-baseline justify-between gap-2 border-b bg-[#f1f7f3] px-5 py-3 dark:bg-muted">
        <h2 className="text-[14px] font-medium">Companies, last 30 days</h2>
        <p className="flex items-center gap-1.5 text-[12px] text-muted-foreground">
          <span className="inline-block h-3 w-0.5 rounded-full bg-foreground/60" /> marks the 10% flag line
        </p>
      </header>
      <ul className="divide-y">
        {sorted.map((company) => (
          <CompanyRow key={company.id} company={company} />
        ))}
      </ul>
    </section>
  );
}
