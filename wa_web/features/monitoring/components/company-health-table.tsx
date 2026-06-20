"use client";

import { AlertTriangle, CheckCircle2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { useCompanyHealth } from "@/features/monitoring/hooks/use-monitoring";
import type { CompanyHealth } from "@/features/monitoring/types";

const numFmt = new Intl.NumberFormat("en-US");
const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});

function HealthBadge({ company }: { company: CompanyHealth }) {
  if (!company.isActive) {
    return (
      <span className="flex items-center gap-1 text-xs text-muted-foreground">
        <XCircle className="h-3.5 w-3.5" />
        Inactive
      </span>
    );
  }
  if (company.isFlagged) {
    return (
      <span className="flex items-center gap-1 text-xs font-medium text-amber-600">
        <AlertTriangle className="h-3.5 w-3.5" />
        Flagged
      </span>
    );
  }
  return (
    <span className="flex items-center gap-1 text-xs font-medium text-primary">
      <CheckCircle2 className="h-3.5 w-3.5" />
      Healthy
    </span>
  );
}

function SummaryCards({ companies }: { companies: CompanyHealth[] }) {
  const total     = companies.length;
  const active    = companies.filter((c) => c.isActive).length;
  const flagged   = companies.filter((c) => c.isFlagged && c.isActive).length;
  const totalSent = companies.reduce((s, c) => s + c.totalSent30d, 0);

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {[
        { label: "Total Companies", value: total, color: "text-foreground" },
        { label: "Active", value: active, color: "text-primary" },
        { label: "Flagged (>10% fail)", value: flagged, color: flagged > 0 ? "text-amber-600" : "text-foreground" },
        { label: "Messages Sent (30d)", value: numFmt.format(totalSent), color: "text-foreground" },
      ].map(({ label, value, color }) => (
        <div key={label} className="rounded-xl border bg-card p-5 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
          <p className={`mt-2 text-3xl font-bold tabular-nums ${color}`}>{value}</p>
        </div>
      ))}
    </div>
  );
}

export function CompanyHealthTable() {
  const { data, isLoading } = useCompanyHealth();

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-xl" />
          ))}
        </div>
        <div className="space-y-2">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-full rounded" />
          ))}
        </div>
      </div>
    );
  }

  const companies = data?.companies ?? [];

  // Sort: flagged + active first, then active, then inactive
  const sorted = [...companies].sort((a, b) => {
    const scoreA = a.isActive ? (a.isFlagged ? 2 : 1) : 0;
    const scoreB = b.isActive ? (b.isFlagged ? 2 : 1) : 0;
    if (scoreB !== scoreA) return scoreB - scoreA;
    return b.totalSent30d - a.totalSent30d;
  });

  return (
    <div className="flex flex-col gap-6">
      <SummaryCards companies={companies} />

      <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/40">
                <th className="px-4 py-3 text-left font-medium text-muted-foreground">Company</th>
                <th className="px-4 py-3 text-left font-medium text-muted-foreground">Health</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Sent (30d)</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Failed</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Fail Rate</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Billable</th>
                <th className="px-4 py-3 text-right font-medium text-muted-foreground">Active Campaigns</th>
                <th className="px-4 py-3 text-left font-medium text-muted-foreground">Last Activity</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((company) => (
                <tr
                  key={company.id}
                  className={`border-b last:border-0 transition-colors hover:bg-muted/20 ${
                    !company.isActive ? "opacity-50" : ""
                  }`}
                >
                  <td className="px-4 py-3">
                    <div className="flex flex-col">
                      <span className="font-medium">{company.name}</span>
                      {!company.isActive && (
                        <Badge variant="secondary" className="mt-0.5 w-fit text-[10px]">
                          Inactive
                        </Badge>
                      )}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <HealthBadge company={company} />
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {numFmt.format(company.totalSent30d)}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums text-destructive">
                    {numFmt.format(company.failed30d)}
                  </td>
                  <td className={`px-4 py-3 text-right tabular-nums font-medium ${
                    company.isFlagged ? "text-amber-600" : "text-muted-foreground"
                  }`}>
                    {company.totalSent30d > 0
                      ? `${(company.failureRate * 100).toFixed(1)}%`
                      : "—"}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums text-primary">
                    {numFmt.format(company.billable30d)}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">
                    {company.activeCampaigns > 0 ? (
                      <Badge variant="secondary">{company.activeCampaigns}</Badge>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {company.lastMessageAt
                      ? dateFmt.format(new Date(company.lastMessageAt))
                      : "Never"}
                  </td>
                </tr>
              ))}
              {sorted.length === 0 && (
                <tr>
                  <td colSpan={8} className="px-4 py-10 text-center text-muted-foreground">
                    No companies found.
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
