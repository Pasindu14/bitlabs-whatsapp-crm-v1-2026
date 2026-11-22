"use client";

import { useState } from "react";
import { Megaphone, MessagesSquare, type LucideIcon } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { CampaignPerformanceTable } from "@/features/analytics/components/campaign-performance-table";
import { AnalyticsDateRangePicker, lastDays } from "@/features/analytics/components/date-range-picker";
import { useMessageMetrics, useCampaignPerformance } from "@/features/analytics/hooks/use-analytics";
import { DeliveryBand } from "@/features/dashboard/components/delivery-band";
import { ActivityChart } from "@/features/dashboard/components/activity-chart";

const shortDate = new Intl.DateTimeFormat("en-US", { day: "numeric", month: "short" });

/** "last 30 days" for a preset, otherwise "12 Oct to 22 Nov". */
function rangeLabel(from: string, to: string): string {
  for (const d of [7, 30, 90]) {
    const r = lastDays(d);
    if (r.from === from && r.to === to) return `last ${d} days`;
  }
  return `${shortDate.format(new Date(`${from}T00:00:00`))} to ${shortDate.format(new Date(`${to}T00:00:00`))}`;
}

/**
 * Reports. The messages view is the dashboard's delivery band and activity chart over any range,
 * so the two screens tell the same story in the same shapes.
 */
export function AnalyticsDashboard() {
  const [range, setRange] = useState(() => lastDays(30));

  const messages = useMessageMetrics(range.from, range.to);
  const campaigns = useCampaignPerformance(range.from, range.to);
  const m = messages.data;

  return (
    <Tabs defaultValue="messages" className="gap-5">
      <div className="flex flex-wrap items-end justify-between gap-3 border-b">
        <TabsList variant="line" className="h-auto gap-1 pb-0">
          <ReportTab value="messages" icon={MessagesSquare} label="Messages" />
          <ReportTab value="campaigns" icon={Megaphone} label="Campaigns" />
        </TabsList>
        <div className="pb-2">
          <AnalyticsDateRangePicker from={range.from} to={range.to} onChange={setRange} />
        </div>
      </div>

      <TabsContent value="messages" className="flex flex-col gap-4">
        {messages.isLoading ? (
          <Skeleton className="h-60 w-full rounded-2xl" />
        ) : messages.isError ? (
          <p className="rounded-xl border bg-card px-4 py-10 text-center text-sm text-muted-foreground">
            Message numbers couldn&apos;t load. Refresh the page to try again.
          </p>
        ) : (
          <DeliveryBand
            title={`Your messages, ${rangeLabel(range.from, range.to)}`}
            sent={m?.totalSent ?? 0}
            delivered={m?.totalDelivered ?? 0}
            read={m?.totalRead ?? 0}
            failed={m?.totalFailed ?? 0}
            trend={null}
            failedHint="See which campaigns in the Campaigns tab"
          />
        )}
        <ActivityChart daily={m?.daily ?? []} range={range} />
      </TabsContent>

      <TabsContent value="campaigns">
        <CampaignPerformanceTable data={campaigns.data} isLoading={campaigns.isLoading} />
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
