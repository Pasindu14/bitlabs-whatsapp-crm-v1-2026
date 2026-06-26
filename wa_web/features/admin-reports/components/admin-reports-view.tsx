"use client";

import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { DashboardCards } from "@/features/admin-reports/components/dashboard-cards";
import { PackagesReport } from "@/features/admin-reports/components/packages-report";
import { BalancesReport } from "@/features/admin-reports/components/balances-report";
import { UsageReport } from "@/features/admin-reports/components/usage-report";

export function AdminReportsView() {
  return (
    <Tabs defaultValue="dashboard" className="w-full">
      <TabsList>
        <TabsTrigger value="dashboard">Dashboard</TabsTrigger>
        <TabsTrigger value="packages">Packages</TabsTrigger>
        <TabsTrigger value="balances">Balances</TabsTrigger>
        <TabsTrigger value="usage">Usage</TabsTrigger>
      </TabsList>

      <TabsContent value="dashboard" className="mt-6">
        <DashboardCards />
      </TabsContent>
      <TabsContent value="packages" className="mt-6">
        <PackagesReport />
      </TabsContent>
      <TabsContent value="balances" className="mt-6">
        <BalancesReport />
      </TabsContent>
      <TabsContent value="usage" className="mt-6">
        <UsageReport />
      </TabsContent>
    </Tabs>
  );
}
