"use client";

import { BarChart3, LayoutGrid, Package, Wallet, type LucideIcon } from "lucide-react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { DashboardCards } from "@/features/admin-reports/components/dashboard-cards";
import { PackagesReport } from "@/features/admin-reports/components/packages-report";
import { BalancesReport } from "@/features/admin-reports/components/balances-report";
import { UsageReport } from "@/features/admin-reports/components/usage-report";

export function AdminReportsView() {
  return (
    <Tabs defaultValue="dashboard" className="w-full gap-5">
      <TabsList variant="line" className="h-auto w-full justify-start gap-1 border-b pb-0">
        <ReportTab value="dashboard" icon={LayoutGrid} label="Overview" />
        <ReportTab value="packages" icon={Package} label="Packages sold" />
        <ReportTab value="balances" icon={Wallet} label="Balances" />
        <ReportTab value="usage" icon={BarChart3} label="Usage and cost" />
      </TabsList>

      <TabsContent value="dashboard">
        <DashboardCards />
      </TabsContent>
      <TabsContent value="packages">
        <PackagesReport />
      </TabsContent>
      <TabsContent value="balances">
        <BalancesReport />
      </TabsContent>
      <TabsContent value="usage">
        <UsageReport />
      </TabsContent>
    </Tabs>
  );
}

function ReportTab({ value, icon: Icon, label }: { value: string; icon: LucideIcon; label: string }) {
  return (
    <TabsTrigger
      value={value}
      className="-mb-px h-10 flex-none rounded-none border-0 border-b-2 border-transparent px-3 text-[13px] font-medium text-muted-foreground data-active:border-primary data-active:text-foreground data-[state=active]:border-primary data-[state=active]:text-foreground"
    >
      <Icon data-icon="inline-start" className="size-4" />
      {label}
    </TabsTrigger>
  );
}
