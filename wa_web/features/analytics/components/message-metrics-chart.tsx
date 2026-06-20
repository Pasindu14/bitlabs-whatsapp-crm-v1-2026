"use client";

import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import type { MessageMetrics } from "@/features/analytics/types";

interface Props {
  data?: MessageMetrics;
  isLoading: boolean;
}

export function MessageMetricsChart({ data, isLoading }: Props) {
  if (isLoading) {
    return <Skeleton className="h-64 w-full rounded-xl" />;
  }

  const chartData = data?.daily ?? [];

  if (chartData.length === 0) {
    return (
      <div className="flex h-64 items-center justify-center rounded-xl border bg-card text-sm text-muted-foreground">
        No message data for this period.
      </div>
    );
  }

  return (
    <div className="rounded-xl border bg-card p-5 shadow-sm">
      <p className="mb-4 text-sm font-semibold">Daily Message Activity</p>
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={chartData} margin={{ top: 4, right: 16, left: 0, bottom: 4 }}>
          <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
          <XAxis
            dataKey="date"
            tick={{ fontSize: 11 }}
            tickFormatter={(v: string) => v.slice(5)}
          />
          <YAxis tick={{ fontSize: 11 }} width={36} />
          <Tooltip
            contentStyle={{ fontSize: 12, borderRadius: 8 }}
            labelFormatter={(label) => `Date: ${label}`}
          />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          <Bar dataKey="sent" name="Sent" fill="#008236" radius={[2, 2, 0, 0]} />
          <Bar dataKey="delivered" name="Delivered" fill="#3b82f6" radius={[2, 2, 0, 0]} />
          <Bar dataKey="read" name="Read" fill="#8b5cf6" radius={[2, 2, 0, 0]} />
          <Bar dataKey="failed" name="Failed" fill="#ef4444" radius={[2, 2, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
