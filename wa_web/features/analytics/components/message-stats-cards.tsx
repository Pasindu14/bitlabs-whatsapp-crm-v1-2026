"use client";

import { Skeleton } from "@/components/ui/skeleton";
import type { MessageMetrics } from "@/features/analytics/types";

interface Props {
  data?: MessageMetrics;
  isLoading: boolean;
}

const numFmt = new Intl.NumberFormat("en-US");

interface StatCardProps {
  label: string;
  value: number;
  color: string;
  isLoading: boolean;
}

function StatCard({ label, value, color, isLoading }: StatCardProps) {
  return (
    <div className="rounded-xl border bg-card p-5 shadow-sm">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      {isLoading ? (
        <Skeleton className="mt-2 h-8 w-24" />
      ) : (
        <p className={`mt-2 text-3xl font-bold tabular-nums ${color}`}>
          {numFmt.format(value)}
        </p>
      )}
    </div>
  );
}

export function MessageStatsCards({ data, isLoading }: Props) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <StatCard label="Total Sent" value={data?.totalSent ?? 0} color="text-foreground" isLoading={isLoading} />
      <StatCard label="Delivered" value={data?.totalDelivered ?? 0} color="text-primary" isLoading={isLoading} />
      <StatCard label="Read" value={data?.totalRead ?? 0} color="text-blue-600" isLoading={isLoading} />
      <StatCard label="Failed" value={data?.totalFailed ?? 0} color="text-destructive" isLoading={isLoading} />
    </div>
  );
}
