"use client";

import { Info } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Popover,
  PopoverContent,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@/components/ui/popover";
import type { CampaignPerformance } from "@/features/analytics/types";
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from "@/components/ui/table";
import { Button } from "@/components/ui/button";

interface Props {
  data?: CampaignPerformance;
  isLoading: boolean;
}

const numFmt = new Intl.NumberFormat("en-US");
const dateFmt = new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric", year: "numeric" });

function pct(part: number, total: number): string {
  if (total === 0) return "—";
  return `${Math.round((part / total) * 100)}%`;
}

const STATUS_VARIANT: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Completed: "default",
  Running: "secondary",
  Failed: "destructive",
  Cancelled: "outline",
  Paused: "outline",
  Scheduled: "outline",
};

// Plain-language definitions shown in the header info popovers, so it's clear why the three
// percentages don't sum to 100% (Delivered includes Read; Failed is its own separate bucket).
const METRIC_INFO: Record<string, string> = {
  "Delivered %":
    "Share of recipients whose message reached their device — this includes messages that were also Read, since a Read message was necessarily delivered first. Calculated as (Delivered + Read) ÷ Recipients.",
  "Read %":
    "Share of recipients who opened the message (WhatsApp read receipt). Calculated as Read ÷ Recipients.",
  "Failed %":
    "Share of recipients WhatsApp could not deliver to — invalid number, opted out, blocked, or rejected by Meta. Calculated as Failed ÷ Recipients.",
};

function MetricHeader({ label, align }: { label: string; align: "left" | "right" }) {
  return (
    <span className={`inline-flex items-center gap-1 ${align === "right" ? "justify-end" : ""}`}>
      {label}
      <Popover>
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="icon-xs"
            className="size-5 text-muted-foreground/70"
            aria-label={`What is ${label}?`}
          >
            <Info className="size-3.5" />
          </Button>
        </PopoverTrigger>
        <PopoverContent align={align === "right" ? "end" : "start"} className="w-72 gap-2">
          <PopoverHeader>
            <PopoverTitle>{label}</PopoverTitle>
          </PopoverHeader>
          <p className="text-sm text-muted-foreground">{METRIC_INFO[label]}</p>
        </PopoverContent>
      </Popover>
    </span>
  );
}

export function CampaignPerformanceTable({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-10 w-full rounded" />
        ))}
      </div>
    );
  }

  const rows = data?.campaigns ?? [];

  if (rows.length === 0) {
    return (
      <div className="flex h-40 items-center justify-center rounded-xl border bg-card text-sm text-muted-foreground">
        No campaigns launched in this period.
      </div>
    );
  }

  return (
    <div className="rounded-xl border bg-card shadow-sm overflow-hidden">
      <div className="overflow-x-auto">
        <Table className="w-full text-sm">
          <TableHeader>
            <TableRow className="border-b bg-muted/40">
              <TableHead className="px-4 py-3 text-left font-medium text-muted-foreground">Campaign</TableHead>
              <TableHead className="px-4 py-3 text-left font-medium text-muted-foreground">Status</TableHead>
              <TableHead className="px-4 py-3 text-left font-medium text-muted-foreground">Launched</TableHead>
              <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">Recipients</TableHead>
              <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">
                <MetricHeader label="Delivered %" align="right" />
              </TableHead>
              <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">
                <MetricHeader label="Read %" align="right" />
              </TableHead>
              <TableHead className="px-4 py-3 text-right font-medium text-muted-foreground">
                <MetricHeader label="Failed %" align="right" />
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id} className="border-b last:border-0 hover:bg-muted/20 transition-colors">
                <TableCell className="px-4 py-3 font-medium max-w-[200px] truncate">{row.name}</TableCell>
                <TableCell className="px-4 py-3">
                  <Badge variant={STATUS_VARIANT[row.status] ?? "secondary"}>
                    {row.status}
                  </Badge>
                </TableCell>
                <TableCell className="px-4 py-3 text-muted-foreground tabular-nums">
                  {row.launchedAt ? dateFmt.format(new Date(row.launchedAt)) : "—"}
                </TableCell>
                <TableCell className="px-4 py-3 text-right tabular-nums">
                  {numFmt.format(row.totalRecipients)}
                </TableCell>
                <TableCell className="px-4 py-3 text-right tabular-nums text-primary">
                  {pct(row.delivered + row.read, row.totalRecipients)}
                </TableCell>
                <TableCell className="px-4 py-3 text-right tabular-nums text-blue-600">
                  {pct(row.read, row.totalRecipients)}
                </TableCell>
                <TableCell className="px-4 py-3 text-right tabular-nums text-destructive">
                  {pct(row.failed, row.totalRecipients)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
