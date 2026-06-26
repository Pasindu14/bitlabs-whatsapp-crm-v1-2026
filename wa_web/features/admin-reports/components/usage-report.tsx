"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useUsageReport } from "@/features/admin-reports/hooks/use-admin-reports";
import {
  DateRangeFilter,
  exportCsv,
  isoDaysAgo,
  isoToday,
  numFmt,
} from "@/features/admin-reports/components/shared";

export function UsageReport() {
  const [from, setFrom] = useState(isoDaysAgo(30));
  const [to, setTo] = useState(isoToday());
  const { data, isLoading } = useUsageReport(from, to);

  const applyPreset = (days: number) => {
    setFrom(isoDaysAgo(days));
    setTo(isoToday());
  };

  const companies = data?.companies ?? [];

  const handleExport = () => {
    exportCsv(
      `usage_${from}_to_${to}.csv`,
      ["Company", "Sent", "Delivered", "Read", "Failed", "Billable"],
      companies.map((c) => [c.companyName, c.sent, c.delivered, c.read, c.failed, c.billable])
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

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="rounded-xl border bg-card p-5 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total Sent</p>
          <p className="mt-2 text-3xl font-bold tabular-nums">
            {isLoading ? "—" : numFmt.format(data?.totalSent ?? 0)}
          </p>
        </div>
        <div className="rounded-xl border bg-card p-5 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total Billable</p>
          <p className="mt-2 text-3xl font-bold tabular-nums text-primary">
            {isLoading ? "—" : numFmt.format(data?.totalBillable ?? 0)}
          </p>
        </div>
      </div>

      {isLoading ? (
        <Skeleton className="h-64 w-full rounded-xl" />
      ) : (
        <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b bg-muted/40">
                  <th className="px-4 py-3 text-left font-medium text-muted-foreground">Company</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Sent</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Delivered</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Read</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Failed</th>
                  <th className="px-4 py-3 text-right font-medium text-muted-foreground">Billable</th>
                </tr>
              </thead>
              <tbody>
                {companies.map((c) => (
                  <tr key={c.companyId} className="border-b last:border-0 hover:bg-muted/20">
                    <td className="px-4 py-3 font-medium">{c.companyName}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(c.sent)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(c.delivered)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(c.read)}</td>
                    <td className="px-4 py-3 text-right tabular-nums text-destructive">
                      {numFmt.format(c.failed)}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums text-primary">
                      {numFmt.format(c.billable)}
                    </td>
                  </tr>
                ))}
                {companies.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-4 py-10 text-center text-muted-foreground">
                      No outbound messages in this range.
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
