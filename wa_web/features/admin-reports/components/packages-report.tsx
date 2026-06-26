"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { usePackagesReport } from "@/features/admin-reports/hooks/use-admin-reports";
import {
  DateRangeFilter,
  exportCsv,
  isoDaysAgo,
  isoToday,
  money,
  numFmt,
} from "@/features/admin-reports/components/shared";

export function PackagesReport() {
  const [from, setFrom] = useState(isoDaysAgo(30));
  const [to, setTo] = useState(isoToday());
  const { data, isLoading } = usePackagesReport(from, to);

  const applyPreset = (days: number) => {
    setFrom(isoDaysAgo(days));
    setTo(isoToday());
  };

  const byPlan = data?.byPlan ?? [];
  const maxRevenue = Math.max(1, ...byPlan.map((r) => r.revenue));

  const handleExport = () => {
    exportCsv(
      `packages_${from}_to_${to}.csv`,
      ["Plan", "Price", "Currency", "Count", "Revenue"],
      byPlan.map((r) => [r.planName, r.price, r.currency, r.count, r.revenue])
    );
  };

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <DateRangeFilter from={from} to={to} onFrom={setFrom} onTo={setTo} onPreset={applyPreset} />
        <Button variant="outline" size="sm" onClick={handleExport} disabled={byPlan.length === 0}>
          <Download className="mr-1.5 h-4 w-4" />
          Export CSV
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="rounded-xl border bg-card p-5 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total Packages</p>
          <p className="mt-2 text-3xl font-bold tabular-nums">
            {isLoading ? "—" : numFmt.format(data?.totalPackages ?? 0)}
          </p>
        </div>
        <div className="rounded-xl border bg-card p-5 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total Revenue</p>
          <p className="mt-2 text-3xl font-bold tabular-nums text-primary">
            {isLoading ? "—" : money(data?.totalRevenue ?? 0)}
          </p>
        </div>
      </div>

      {isLoading ? (
        <Skeleton className="h-48 w-full rounded-xl" />
      ) : (
        <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b bg-muted/40">
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Plan</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Price</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Packages</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Revenue</th>
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground w-1/3">Share</th>
                </tr>
              </thead>
              <tbody>
                {byPlan.map((r) => (
                  <tr key={r.planId} className="border-b last:border-0 hover:bg-muted/20">
                    <td className="px-4 py-3 font-medium">{r.planName}</td>
                    <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">
                      {money(r.price, r.currency)}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(r.count)}</td>
                    <td className="px-4 py-3 text-right tabular-nums text-primary">
                      {money(r.revenue, r.currency)}
                    </td>
                    <td className="px-4 py-3">
                      <div className="h-2 rounded-full bg-muted">
                        <div
                          className="h-2 rounded-full bg-primary"
                          style={{ width: `${(r.revenue / maxRevenue) * 100}%` }}
                        />
                      </div>
                    </td>
                  </tr>
                ))}
                {byPlan.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-4 py-10 text-center text-muted-foreground">
                      No packages purchased in this range.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
