"use client";

import { useMemo, useState } from "react";
import { Area, AreaChart, CartesianGrid, XAxis, YAxis } from "recharts";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import type { DailyMessageCount } from "@/features/analytics/types";

const config = {
  sent: { label: "Sent", color: "#0f6b3e" },
  delivered: { label: "Delivered", color: "#8fc9a5" },
  read: { label: "Read", color: "#2fa8e6" },
} satisfies ChartConfig;

const RANGES = [
  { value: "7", label: "7 days" },
  { value: "14", label: "14 days" },
  { value: "30", label: "30 days" },
];

// Daily rows are "YYYY-MM-DD"; parse as local midnight so the label never slips a day.
const toDate = (d: string) => new Date(`${d.slice(0, 10)}T00:00:00`);
const tickFmt = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric" });

/** Messages sent / delivered / read per day, with a 7/14/30-day switch. */
export function ActivityChart({
  daily,
  range: fixedRange,
}: {
  daily: DailyMessageCount[];
  /** A fixed from/to (yyyy-mm-dd). Hides the 7/14/30 switch; the caller owns the range. */
  range?: { from: string; to: string };
}) {
  const [range, setRange] = useState("30");

  // One point per day for the whole range, zeros included, so a quiet week still draws a line
  // instead of a lone dot.
  const data = useMemo(() => {
    const byDay = new Map(daily.map((d) => [d.date.slice(0, 10), d]));
    const out: DailyMessageCount[] = [];
    const key = (d: Date) =>
      `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
    const end = fixedRange ? new Date(`${fixedRange.to}T00:00:00`) : new Date();
    const start = fixedRange
      ? new Date(`${fixedRange.from}T00:00:00`)
      : new Date(end.getFullYear(), end.getMonth(), end.getDate() - (Number(range) - 1));
    // Capped at a year so a mistyped range can't build an enormous series.
    for (let d = new Date(start), n = 0; d <= end && n < 366; d.setDate(d.getDate() + 1), n++) {
      const k = key(d);
      out.push(byDay.get(k) ?? { date: k, sent: 0, delivered: 0, read: 0, failed: 0 });
    }
    return out;
  }, [daily, range, fixedRange]);
  const empty = data.every((d) => d.sent === 0);

  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle className="text-[15px]">Message activity</CardTitle>
        <CardDescription>Template and chat messages sent from your numbers.</CardDescription>
        {!fixedRange && (
        <CardAction>
          <ToggleGroup
            type="single"
            variant="outline"
            size="sm"
            value={range}
            onValueChange={(v) => v && setRange(v)}
          >
            {RANGES.map((r) => (
              <ToggleGroupItem key={r.value} value={r.value} className="px-2.5">
                {r.label}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        </CardAction>
        )}
      </CardHeader>
      <CardContent className="flex-1">
        {empty ? (
          <div className="flex h-[260px] items-center justify-center rounded-md border border-dashed text-muted-foreground">
            No messages sent in this period yet.
          </div>
        ) : (
          <ChartContainer config={config} className="aspect-auto h-[260px] w-full">
            <AreaChart data={data} margin={{ left: 0, right: 8, top: 8 }}>
              <defs>
                {(Object.keys(config) as (keyof typeof config)[]).map((key) => (
                  <linearGradient key={key} id={`fill-${key}`} x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor={`var(--color-${key})`} stopOpacity={0.35} />
                    <stop offset="95%" stopColor={`var(--color-${key})`} stopOpacity={0.02} />
                  </linearGradient>
                ))}
              </defs>
              <CartesianGrid vertical={false} />
              <XAxis
                dataKey="date"
                tickLine={false}
                axisLine={false}
                tickMargin={8}
                minTickGap={28}
                tickFormatter={(v: string) => tickFmt.format(toDate(v))}
              />
              <YAxis tickLine={false} axisLine={false} width={36} allowDecimals={false} />
              <ChartTooltip
                cursor={false}
                content={
                  <ChartTooltipContent
                    indicator="dot"
                    labelFormatter={(v) => tickFmt.format(toDate(String(v)))}
                  />
                }
              />
              <Area dataKey="sent" type="monotone" fill="url(#fill-sent)" stroke="var(--color-sent)" strokeWidth={2} />
              <Area
                dataKey="delivered"
                type="monotone"
                fill="url(#fill-delivered)"
                stroke="var(--color-delivered)"
                strokeWidth={1.5}
              />
              <Area dataKey="read" type="monotone" fill="url(#fill-read)" stroke="var(--color-read)" strokeWidth={1.5} />
              <ChartLegend content={<ChartLegendContent />} />
            </AreaChart>
          </ChartContainer>
        )}
      </CardContent>
    </Card>
  );
}
