"use client";

import { useState } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { MessageStatsCards } from "@/features/analytics/components/message-stats-cards";
import { MessageMetricsChart } from "@/features/analytics/components/message-metrics-chart";
import { CampaignPerformanceTable } from "@/features/analytics/components/campaign-performance-table";
import { CostBreakdownChart } from "@/features/analytics/components/cost-breakdown-chart";
import { AnalyticsDateRangePicker } from "@/features/analytics/components/date-range-picker";
import { useMessageMetrics, useCampaignPerformance, useCostAnalytics } from "@/features/analytics/hooks/use-analytics";

function defaultRange() {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 29);
  return {
    from: from.toISOString().slice(0, 10),
    to: to.toISOString().slice(0, 10),
  };
}

export function AnalyticsDashboard() {
  const [range, setRange] = useState(defaultRange);

  const messages = useMessageMetrics(range.from, range.to);
  const campaigns = useCampaignPerformance(range.from, range.to);
  const costs = useCostAnalytics(range.from, range.to);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-end">
        <AnalyticsDateRangePicker
          from={range.from}
          to={range.to}
          onChange={setRange}
        />
      </div>

      <Tabs defaultValue="messages">
        <TabsList className="mb-4">
          <TabsTrigger value="messages">Messages</TabsTrigger>
          <TabsTrigger value="campaigns">Campaigns</TabsTrigger>
          <TabsTrigger value="costs">Costs</TabsTrigger>
        </TabsList>

        <TabsContent value="messages" className="flex flex-col gap-6">
          <MessageStatsCards data={messages.data} isLoading={messages.isLoading} />
          <MessageMetricsChart data={messages.data} isLoading={messages.isLoading} />
        </TabsContent>

        <TabsContent value="campaigns">
          <CampaignPerformanceTable
            data={campaigns.data}
            isLoading={campaigns.isLoading}
          />
        </TabsContent>

        <TabsContent value="costs">
          <CostBreakdownChart data={costs.data} isLoading={costs.isLoading} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
