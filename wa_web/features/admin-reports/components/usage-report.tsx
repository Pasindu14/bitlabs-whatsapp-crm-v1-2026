"use client";

import { Fragment, useState } from "react";
import { ChevronDown, ChevronRight, Download, TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useUsageReport } from "@/features/admin-reports/hooks/use-admin-reports";
import type { CompanyUsageRow, UsageCost } from "@/features/admin-reports/types";
import {
  DateRangeFilter,
  exportCsv,
  isoDaysAgo,
  isoToday,
  numFmt,
} from "@/features/admin-reports/components/shared";
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from "@/components/ui/table";

/** A company costing us more than this share of what it pays is flagged. */
const COST_SHARE_ALERT = 0.5;

const usdFmt = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
});
const aedFmt = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const aedPerMsgFmt = new Intl.NumberFormat("en-US", { minimumFractionDigits: 3, maximumFractionDigits: 3 });
const pctFmt = new Intl.NumberFormat("en-US", { style: "percent", maximumFractionDigits: 0 });

const KIND_LABEL: Record<string, string> = {
  Reply: "Replies",
  FollowUp: "Follow-ups",
  TemplateVariables: "Template variables",
  Preview: "Bot preview (tests)",
  BookingSummary: "Booking summaries (clinic CRM)",
};

const aed = (v: number | null | undefined) => (v == null ? "—" : `AED ${aedFmt.format(v)}`);

function isOverShare(cost: UsageCost | null) {
  return cost?.costShareOfPlan != null && cost.costShareOfPlan > COST_SHARE_ALERT;
}

export function UsageReport() {
  const [from, setFrom] = useState(isoDaysAgo(30));
  const [to, setTo] = useState(isoToday());
  const [open, setOpen] = useState<string | null>(null);
  const { data, isLoading } = useUsageReport(from, to);

  const applyPreset = (days: number) => {
    setFrom(isoDaysAgo(days));
    setTo(isoToday());
  };

  const companies = data?.companies ?? [];
  const totalSent = data?.totalSent ?? 0;
  const totalCostAed = data?.totalCostAed ?? 0;
  const flagged = companies.filter((c) => isOverShare(c.cost)).length;

  const handleExport = () => {
    exportCsv(
      `usage_${from}_to_${to}.csv`,
      [
        "Company", "Sent", "Delivered", "Read", "Failed",
        "AI cost (USD)", "Meta cost (USD)", "Total cost (AED)", "Cost / message (AED)",
        "Plan", "Plan price for range (AED)", "Margin (AED)", "Cost share of plan",
      ],
      companies.map((c) => [
        c.companyName, c.sent, c.delivered, c.read, c.failed,
        c.cost?.aiCostUsd ?? 0,
        c.cost?.metaCostUsd ?? 0,
        c.cost?.totalCostAed ?? 0,
        c.cost?.costPerMessageAed ?? "",
        c.cost?.planName ?? "",
        c.cost?.planPriceForRangeAed ?? "",
        c.cost?.marginAed ?? "",
        c.cost?.costShareOfPlan != null ? c.cost.costShareOfPlan.toFixed(3) : "",
      ])
    );
  };

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <DateRangeFilter from={from} to={to} onFrom={setFrom} onTo={setTo} onPreset={applyPreset} />
        <Button variant="outline" size="sm" onClick={handleExport} disabled={companies.length === 0}>
          <Download className="mr-1.5 h-4 w-4" />
          Export CSV
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="Total sent" value={isLoading ? "—" : numFmt.format(totalSent)} />
        <StatCard
          label="Total cost"
          value={isLoading ? "—" : aed(totalCostAed)}
          hint={
            isLoading
              ? undefined
              : `AI ${usdFmt.format(data?.totalAiCostUsd ?? 0)} · Meta ${usdFmt.format(data?.totalMetaCostUsd ?? 0)}`
          }
        />
        <StatCard
          label="Cost per message"
          value={isLoading || totalSent === 0 ? "—" : `AED ${aedPerMsgFmt.format(totalCostAed / totalSent)}`}
          hint="AI + Meta, all companies"
        />
        <StatCard
          label="Over 50% of plan"
          value={isLoading ? "—" : numFmt.format(flagged)}
          hint="Companies whose cost is eating their margin"
          alert={flagged > 0}
        />
      </div>

      {isLoading ? (
        <Skeleton className="h-64 w-full rounded-xl" />
      ) : (
        <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
          <div className="overflow-x-auto">
            <Table className="w-full text-sm">
              <TableHeader>
                <TableRow className="border-b bg-muted/40">
                  <TableHead className="w-8 px-2 py-3" />
                  <TableHead className="px-4 py-3 text-left font-medium text-muted-foreground">Company</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Sent</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Failed</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">AI cost</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Meta cost</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Total</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Per msg</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Plan (pro-rated)</TableHead>
                  <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Margin</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {companies.map((c) => (
                  <UsageRow
                    key={c.companyId}
                    row={c}
                    expanded={open === c.companyId}
                    onToggle={() => setOpen(open === c.companyId ? null : c.companyId)}
                  />
                ))}
                {companies.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={10} className="px-4 py-10 text-center text-muted-foreground">
                      No messages or AI usage in this range.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </div>
      )}

      {!isLoading && data && (
        <p className="text-xs text-muted-foreground">
          Costs are stored in USD when each call or message is priced, and shown in AED at{" "}
          {data.usdToAed}. The plan price is pro-rated to the selected range (days ÷ 30). Meta costs are
          estimated from the configured rate card, using Meta&apos;s own billable flag.
        </p>
      )}
    </div>
  );
}

function StatCard({ label, value, hint, alert }: { label: string; value: string; hint?: string; alert?: boolean }) {
  return (
    <div className={cn("rounded-xl border bg-card p-5 shadow-sm", alert && "border-destructive/40 bg-destructive/5")}>
      <p className="text-[13px] font-medium text-muted-foreground">{label}</p>
      <p className={cn("mt-2 text-3xl font-bold tabular-nums", alert && "text-destructive")}>{value}</p>
      {hint && <p className="mt-1 text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}

function UsageRow({ row, expanded, onToggle }: { row: CompanyUsageRow; expanded: boolean; onToggle: () => void }) {
  const cost = row.cost;
  const over = isOverShare(cost);
  const underReported = (cost?.unpricedAiCalls ?? 0) > 0 || (cost?.unpricedMetaMessages ?? 0) > 0;

  return (
    <Fragment>
      <TableRow
        className={cn("border-b last:border-0 hover:bg-muted/20 cursor-pointer", over && "bg-destructive/5")}
        onClick={onToggle}
      >
        <TableCell className="px-2 py-3 text-muted-foreground">
          {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </TableCell>
        <TableCell className="px-4 py-3 font-medium">
          <span className="inline-flex items-center gap-1.5">
            {row.companyName}
            {underReported && (
              <TriangleAlert
                className="h-3.5 w-3.5 text-amber-500"
                aria-label="Some usage has no price yet, so this cost is understated"
              />
            )}
          </span>
        </TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums">{numFmt.format(row.sent)}</TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums text-destructive">{numFmt.format(row.failed)}</TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums">{usdFmt.format(cost?.aiCostUsd ?? 0)}</TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums">{usdFmt.format(cost?.metaCostUsd ?? 0)}</TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums font-medium">{aed(cost?.totalCostAed)}</TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums">
          {cost?.costPerMessageAed == null ? "—" : aedPerMsgFmt.format(cost.costPerMessageAed)}
        </TableCell>
        <TableCell className="px-4 py-3 text-right tabular-nums">
          {cost?.planName ? (
            <span className="flex flex-col items-end">
              <span>{aed(cost.planPriceForRangeAed)}</span>
              <span className="text-xs text-muted-foreground">{cost.planName}</span>
            </span>
          ) : (
            <span className="text-muted-foreground">No plan</span>
          )}
        </TableCell>
        <TableCell
          className={cn(
            "px-4 py-3 text-right tabular-nums font-medium",
            cost?.marginAed != null && cost.marginAed < 0 && "text-destructive",
            cost?.marginAed != null && cost.marginAed >= 0 && !over && "text-primary"
          )}
        >
          {aed(cost?.marginAed)}
          {cost?.costShareOfPlan != null && (
            <span className={cn("block text-xs", over ? "text-destructive" : "text-muted-foreground")}>
              cost {pctFmt.format(cost.costShareOfPlan)} of plan
            </span>
          )}
        </TableCell>
      </TableRow>
      {expanded && (
        <TableRow className="border-b bg-muted/10">
          <TableCell />
          <TableCell colSpan={9} className="px-4 py-4">
            <CostBreakdown row={row} />
          </TableCell>
        </TableRow>
      )}
    </Fragment>
  );
}

function CostBreakdown({ row }: { row: CompanyUsageRow }) {
  const cost = row.cost;
  if (!cost) return <p className="text-muted-foreground">No cost data.</p>;

  return (
    <div className="grid gap-6 md:grid-cols-3">
      <div>
        <p className="mb-2 text-[13px] font-medium text-muted-foreground">AI calls by kind</p>
        {cost.aiByKind.length === 0 ? (
          <p className="text-muted-foreground">No AI calls.</p>
        ) : (
          <ul className="space-y-1">
            {cost.aiByKind.map((k) => (
              <li key={k.kind} className="flex justify-between gap-4 tabular-nums">
                <span>
                  {KIND_LABEL[k.kind] ?? k.kind}{" "}
                  <span className="text-muted-foreground">× {numFmt.format(k.calls)}</span>
                </span>
                <span>{usdFmt.format(k.costUsd)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
      <div>
        <p className="mb-2 text-[13px] font-medium text-muted-foreground">Prompt cache</p>
        <p className="text-2xl font-semibold tabular-nums">
          {cost.cacheHitRate == null ? "—" : pctFmt.format(cost.cacheHitRate)}
        </p>
        <p className="text-xs text-muted-foreground">
          Share of the cached prompt that was reused rather than re-stored. Low means the 5-minute cache keeps
          expiring between messages, which is what makes a quiet clinic&apos;s replies cost more.
        </p>
      </div>
      <div>
        <p className="mb-2 text-[13px] font-medium text-muted-foreground">Meta</p>
        <ul className="space-y-1 tabular-nums">
          <li className="flex justify-between gap-4">
            <span>Billable messages</span>
            <span>{numFmt.format(row.billable)}</span>
          </li>
          <li className="flex justify-between gap-4">
            <span>Estimated cost</span>
            <span>{usdFmt.format(cost.metaCostUsd)}</span>
          </li>
        </ul>
        {(cost.unpricedAiCalls > 0 || cost.unpricedMetaMessages > 0) && (
          <p className="mt-2 text-xs text-amber-600">
            {cost.unpricedAiCalls > 0 && `${cost.unpricedAiCalls} AI call(s) used a model with no configured price. `}
            {cost.unpricedMetaMessages > 0 &&
              `${cost.unpricedMetaMessages} billable message(s) have no price yet. `}
            The totals above are understated.
          </p>
        )}
      </div>
    </div>
  );
}
