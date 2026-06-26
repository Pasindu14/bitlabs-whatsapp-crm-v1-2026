"use client";

import { AlertTriangle, Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useBalancesReport } from "@/features/admin-reports/hooks/use-admin-reports";
import {
  dateFmt,
  exportCsv,
  numFmt,
} from "@/features/admin-reports/components/shared";

export function BalancesReport() {
  const { data, isLoading } = useBalancesReport();
  const companies = data?.companies ?? [];

  const handleExport = () => {
    exportCsv(
      "balances.csv",
      ["Company", "Plan", "Quota", "Used", "Remaining", "Period End", "Low"],
      companies.map((c) => [
        c.companyName,
        c.planName,
        c.monthlyQuota,
        c.used,
        c.remaining,
        c.periodEnd,
        c.isLow ? "yes" : "no",
      ])
    );
  };

  if (isLoading) {
    return <Skeleton className="h-64 w-full rounded-xl" />;
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-end">
        <Button variant="outline" size="sm" onClick={handleExport} disabled={companies.length === 0}>
          <Download className="mr-1.5 h-4 w-4" />
          Export CSV
        </Button>
      </div>

      <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/40">
                <th className="px-4 py-3 text-left font-medium text-muted-foreground">Company</th>
                <th className="px-4 py-3 text-left font-medium text-muted-foreground">Plan</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Quota</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Used</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Remaining</th>
                <th className="px-4 py-3 text-left font-medium text-muted-foreground w-1/4">Usage</th>
                <th className="px-4 py-3 text-left font-medium text-muted-foreground">Period Ends</th>
              </tr>
            </thead>
            <tbody>
              {companies.map((c) => {
                const pct = c.monthlyQuota > 0 ? Math.min(100, (c.used / c.monthlyQuota) * 100) : 0;
                return (
                  <tr
                    key={c.companyId}
                    className={`border-b last:border-0 hover:bg-muted/20 ${
                      c.isLow ? "bg-amber-50/60" : ""
                    }`}
                  >
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-1.5 font-medium">
                        {c.isLow && <AlertTriangle className="h-3.5 w-3.5 text-amber-600" />}
                        {c.companyName}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{c.planName}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(c.monthlyQuota)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(c.used)}</td>
                    <td
                      className={`px-4 py-3 text-right tabular-nums font-medium ${
                        c.isLow ? "text-amber-600" : "text-primary"
                      }`}
                    >
                      {numFmt.format(c.remaining)}
                    </td>
                    <td className="px-4 py-3">
                      <div className="h-2 rounded-full bg-muted">
                        <div
                          className={`h-2 rounded-full ${c.isLow ? "bg-amber-500" : "bg-primary"}`}
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {c.periodEnd ? dateFmt.format(new Date(c.periodEnd)) : "—"}
                    </td>
                  </tr>
                );
              })}
              {companies.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-10 text-center text-muted-foreground">
                    No active subscriptions found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
