"use client";

import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import type { CampaignPerformance } from "@/features/analytics/types";

interface Props {
  data?: CampaignPerformance;
  isLoading: boolean;
}

const numFmt = new Intl.NumberFormat("en-US");
const dateFmt = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric" });

function pct(part: number, total: number): string {
  if (total === 0) return "—";
  return `${Math.round((part / total) * 100)}%`;
}

const STATUS_VARIANT: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Completed: "default",
  Running: "secondary",
  Failed: "destructive",
  Cancelled: "outline",
  Paused: "outline",
  Scheduled: "outline",
};

export function CampaignPerformanceTable({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-10 w-full rounded" />
        ))}
      </div>
    );
  }

  const rows = data?.campaigns ?? [];

  if (rows.length === 0) {
    return (
      <div className="flex h-40 items-center justify-center rounded-xl border bg-card text-sm text-muted-foreground">
        No campaigns launched in this period.
      </div>
    );
  }

  return (
    <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b bg-muted/40">
              <th className="px-4 py-3 text-left font-medium text-muted-foreground">Campaign</th>
              <th className="px-4 py-3 text-left font-medium text-muted-foreground">Status</th>
              <th className="px-4 py-3 text-left font-medium text-muted-foreground">Launched</th>
              <th className="px-4 py-3 text-right font-medium text-muted-foreground">Recipients</th>
              <th className="px-4 py-3 text-right font-medium text-muted-foreground">Delivered %</th>
              <th className="px-4 py-3 text-right font-medium text-muted-foreground">Read %</th>
              <th className="px-4 py-3 text-right font-medium text-muted-foreground">Failed</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} className="border-b last:border-0 hover:bg-muted/20 transition-colors">
                <td className="px-4 py-3 font-medium max-w-[200px] truncate">{row.name}</td>
                <td className="px-4 py-3">
                  <Badge variant={STATUS_VARIANT[row.status] ?? "secondary"}>
                    {row.status}
                  </Badge>
                </td>
                <td className="px-4 py-3 text-muted-foreground tabular-nums">
                  {row.launchedAt ? dateFmt.format(new Date(row.launchedAt)) : "—"}
                </td>
                <td className="px-4 py-3 text-right tabular-nums">
                  {numFmt.format(row.totalRecipients)}
                </td>
                <td className="px-4 py-3 text-right tabular-nums text-primary">
                  {pct(row.delivered + row.read, row.totalRecipients)}
                </td>
                <td className="px-4 py-3 text-right tabular-nums text-blue-600">
                  {pct(row.read, row.totalRecipients)}
                </td>
                <td className="px-4 py-3 text-right tabular-nums text-destructive">
                  {numFmt.format(row.failed)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
