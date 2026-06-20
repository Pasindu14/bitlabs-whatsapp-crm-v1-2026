"use client";

import {
  PieChart,
  Pie,
  Cell,
  Tooltip,
  Legend,
  ResponsiveContainer,
  type PieLabelRenderProps,
} from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import type { CostAnalytics } from "@/features/analytics/types";

interface Props {
  data?: CostAnalytics;
  isLoading: boolean;
}

const numFmt = new Intl.NumberFormat("en-US");

const CATEGORY_COLORS: Record<string, string> = {
  marketing: "#008236",
  utility: "#3b82f6",
  authentication: "#f59e0b",
  service: "#8b5cf6",
  unknown: "#6b7280",
};

function getColor(category: string, billable: boolean): string {
  const base = CATEGORY_COLORS[category.toLowerCase()] ?? CATEGORY_COLORS.unknown;
  return billable ? base : `${base}66`;
}

export function CostBreakdownChart({ data, isLoading }: Props) {
  if (isLoading) {
    return <Skeleton className="h-64 w-full rounded-xl" />;
  }

  const breakdown = data?.breakdown ?? [];

  if (breakdown.length === 0) {
    return (
      <div className="flex h-64 items-center justify-center rounded-xl border bg-card text-sm text-muted-foreground">
        No pricing data available for this period.
      </div>
    );
  }

  const chartData = breakdown.map((b) => ({
    name: `${b.category} (${b.billable ? "billable" : "free"})`,
    value: b.count,
    category: b.category,
    billable: b.billable,
  }));

  return (
    <div className="space-y-4">
      <div className="rounded-xl border bg-card p-5 shadow-sm">
        <p className="mb-4 text-sm font-semibold">Billable vs Non-Billable</p>
        <div className="grid grid-cols-2 gap-4 mb-4">
          <div className="rounded-lg bg-muted/40 p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Billable</p>
            <p className="mt-1 text-2xl font-bold text-primary tabular-nums">
              {numFmt.format(data?.totalBillable ?? 0)}
            </p>
          </div>
          <div className="rounded-lg bg-muted/40 p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Non-Billable</p>
            <p className="mt-1 text-2xl font-bold tabular-nums">
              {numFmt.format(data?.totalNonBillable ?? 0)}
            </p>
          </div>
        </div>
        <ResponsiveContainer width="100%" height={220}>
          <PieChart>
            <Pie
              data={chartData}
              dataKey="value"
              nameKey="name"
              cx="50%"
              cy="50%"
              outerRadius={80}
              label={({ name, percent }: PieLabelRenderProps) =>
                `${String(name ?? "").split(" (")[0]} ${((percent ?? 0) * 100).toFixed(0)}%`
              }
              labelLine={false}
            >
              {chartData.map((entry, index) => (
                <Cell
                  key={`cell-${index}`}
                  fill={getColor(entry.category, entry.billable)}
                />
              ))}
            </Pie>
            <Tooltip
              contentStyle={{ fontSize: 12, borderRadius: 8 }}
              formatter={(value) => [numFmt.format(Number(value ?? 0)), "Messages"]}
            />
            <Legend wrapperStyle={{ fontSize: 12 }} />
          </PieChart>
        </ResponsiveContainer>
      </div>

      <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b bg-muted/40">
              <th className="px-4 py-3 text-left font-medium text-muted-foreground">Category</th>
              <th className="px-4 py-3 text-left font-medium text-muted-foreground">Type</th>
              <th className="px-4 py-3 text-right font-medium text-muted-foreground">Count</th>
            </tr>
          </thead>
          <tbody>
            {breakdown.map((row, i) => (
              <tr key={i} className="border-b last:border-0 hover:bg-muted/20 transition-colors">
                <td className="px-4 py-3 font-medium capitalize">{row.category}</td>
                <td className="px-4 py-3 text-muted-foreground">
                  {row.billable ? "Billable" : "Non-billable"}
                </td>
                <td className="px-4 py-3 text-right tabular-nums">{numFmt.format(row.count)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
