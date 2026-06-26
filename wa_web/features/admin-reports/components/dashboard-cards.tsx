"use client";

import { Skeleton } from "@/components/ui/skeleton";
import { useReportsDashboard } from "@/features/admin-reports/hooks/use-admin-reports";
import { numFmt, money } from "@/features/admin-reports/components/shared";

export function DashboardCards() {
  const { data, isLoading } = useReportsDashboard();

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {Array.from({ length: 8 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-xl" />
        ))}
      </div>
    );
  }

  const d = data;
  const cards = [
    { label: "Total Companies", value: numFmt.format(d?.totalCompanies ?? 0) },
    { label: "Active Companies", value: numFmt.format(d?.activeCompanies ?? 0), color: "text-primary" },
    { label: "Active Subscriptions", value: numFmt.format(d?.activeSubscriptions ?? 0) },
    { label: "Packages Sold (mo)", value: numFmt.format(d?.packagesSoldThisMonth ?? 0) },
    { label: "Revenue (mo)", value: money(d?.revenueThisMonth ?? 0), color: "text-primary" },
    { label: "New Signups (mo)", value: numFmt.format(d?.newSignupsThisMonth ?? 0) },
    { label: "Messages Sent (30d)", value: numFmt.format(d?.messagesSent30d ?? 0) },
    {
      label: "Low Balance Companies",
      value: numFmt.format(d?.companiesLowBalance ?? 0),
      color: (d?.companiesLowBalance ?? 0) > 0 ? "text-amber-600" : "text-foreground",
    },
  ];

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {cards.map(({ label, value, color }) => (
        <div key={label} className="rounded-xl border bg-card p-5 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
          <p className={`mt-2 text-3xl font-bold tabular-nums ${color ?? "text-foreground"}`}>{value}</p>
        </div>
      ))}
    </div>
  );
}
