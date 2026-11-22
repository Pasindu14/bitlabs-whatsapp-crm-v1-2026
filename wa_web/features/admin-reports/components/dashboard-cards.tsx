"use client";

import { Building2, MessagesSquare, Wallet, type LucideIcon } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useReportsDashboard } from "@/features/admin-reports/hooks/use-admin-reports";
import { numFmt, money } from "@/features/admin-reports/components/shared";

/**
 * Platform overview, grouped by the question each number answers — money, companies, messaging —
 * instead of eight identical tiles.
 */
export function DashboardCards() {
  const { data: d, isLoading, isError } = useReportsDashboard();

  if (isLoading) {
    return (
      <div className="grid gap-4 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-56 rounded-2xl" />
        ))}
      </div>
    );
  }

  if (isError || !d) {
    return (
      <p className="rounded-xl border bg-card px-4 py-10 text-center text-sm text-muted-foreground">
        The overview couldn&apos;t load. Refresh the page to try again.
      </p>
    );
  }

  const billableShare = d.messagesSent30d > 0 ? Math.round((d.billableMessages30d / d.messagesSent30d) * 100) : 0;

  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <Panel icon={Wallet} tone="bg-[#dcf1e3] text-[#0f6b3e]" title="Money this month" description="What companies paid since the 1st.">
        <p className="text-[2.75rem] leading-none font-light tracking-[-0.03em] text-[#0f6b3e] tabular-nums dark:text-[#7fd6a0]">
          {money(d.revenueThisMonth)}
        </p>
        <Stats
          items={[{ label: "Packages sold", value: numFmt.format(d.packagesSoldThisMonth) }]}
        />
      </Panel>

      <Panel icon={Building2} tone="bg-[#dcebfa] text-[#1b5e9e]" title="Companies" description="Who is on the platform.">
        <Stats
          items={[
            { label: "Companies", value: numFmt.format(d.totalCompanies), big: true },
            { label: "Active", value: numFmt.format(d.activeCompanies), big: true },
            { label: "On a plan", value: numFmt.format(d.activeSubscriptions) },
            { label: "Joined this month", value: numFmt.format(d.newSignupsThisMonth) },
          ]}
        />
      </Panel>

      <Panel icon={MessagesSquare} tone="bg-[#ede3fa] text-[#5b3a9e]" title="Messaging, last 30 days" description="Across every company.">
        <Stats
          items={[
            { label: "Messages sent", value: numFmt.format(d.messagesSent30d), big: true },
            { label: "Billed by Meta", value: `${numFmt.format(d.billableMessages30d)} (${billableShare}%)`, big: true },
          ]}
        />
        <p
          className={cn(
            "rounded-lg px-3 py-2 text-[12.5px]",
            d.companiesLowBalance > 0 ? "bg-amber-50 text-amber-800" : "bg-muted/60 text-muted-foreground",
          )}
        >
          {d.companiesLowBalance > 0
            ? `${numFmt.format(d.companiesLowBalance)} ${d.companiesLowBalance === 1 ? "company is" : "companies are"} running low on messages. See the Balances tab.`
            : "No company is running low on messages."}
        </p>
      </Panel>
    </div>
  );
}

function Panel({
  icon: Icon,
  tone,
  title,
  description,
  children,
}: {
  icon: LucideIcon;
  tone: string;
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <Card className="rounded-2xl">
      <CardHeader className="flex flex-row items-center gap-3 space-y-0">
        <span className={cn("flex size-9 shrink-0 items-center justify-center rounded-lg", tone)}>
          <Icon className="size-[18px]" />
        </span>
        <div>
          <CardTitle className="text-[15px] font-medium">{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </div>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col gap-5">{children}</CardContent>
    </Card>
  );
}

function Stats({ items }: { items: { label: string; value: string; big?: boolean }[] }) {
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-4">
      {items.map((s) => (
        <div key={s.label}>
          <dt className="text-[12.5px] text-muted-foreground">{s.label}</dt>
          <dd
            className={cn(
              "mt-0.5 tabular-nums",
              s.big ? "text-[1.75rem] leading-none font-light tracking-[-0.02em]" : "text-[15px] font-medium",
            )}
          >
            {s.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}
