"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft, ChevronLeft, ChevronRight } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { Spinner } from "@/components/ui/spinner";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  useCampaign,
  useCampaignStats,
  useCampaignRecipients,
} from "@/features/campaigns/hooks/use-campaigns";
import type { CampaignStatus } from "@/features/campaigns/types";

const PAGE_SIZE = 50;

const CAMPAIGN_STATUS_VARIANT: Record<
  CampaignStatus,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Draft: "secondary",
  Scheduled: "outline",
  Running: "default",
  Paused: "secondary",
  Completed: "default",
  Failed: "destructive",
  Cancelled: "secondary",
};

const RECIPIENT_STATUS_VARIANT: Record<
  string,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Queued: "secondary",
  Sent: "outline",
  Delivered: "default",
  Read: "default",
  Failed: "destructive",
  Skipped: "secondary",
};

// Human labels for the engine's own skip/fail codes; anything else (a raw Meta code) is shown as-is.
const ERROR_LABELS: Record<string, string> = {
  NOT_ON_WHATSAPP: "Not on WhatsApp",
  OPT_OUT: "Opted out",
  NO_CONSENT: "No consent",
  SEND_FAILED: "Send failed",
  JOB_ERROR: "Internal error",
  CAMPAIGN_FAILED: "Campaign failed",
};

function errorLabel(code: string | null): string {
  if (!code) return "—";
  return ERROR_LABELS[code] ?? code;
}

const dateTimeFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

export function CampaignDetail({ campaignId }: { campaignId: string }) {
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const [page, setPage] = useState(1);

  const { data: campaign } = useCampaign(campaignId);
  const { data: stats } = useCampaignStats(campaignId);

  // "Live" while recipients remain queued — drives polling for both stats and the recipient list.
  const isLive = stats ? stats.queued > 0 : false;

  const { data: recipients, isLoading: recipientsLoading } = useCampaignRecipients(
    campaignId,
    page,
    PAGE_SIZE,
    statusFilter,
    isLive
  );

  function changeFilter(value: string) {
    setStatusFilter(value === "all" ? undefined : value);
    setPage(1);
  }

  const total = stats?.totalRecipients ?? 0;
  const processed = stats ? total - stats.queued : 0;
  const percent = total > 0 ? Math.round((processed / total) * 100) : 0;

  const totalPages = recipients?.pagination.totalPages ?? 1;
  const rows = recipients?.items ?? [];

  const TABS: [string, string, number | undefined][] = [
    ["all", "All", stats?.totalRecipients],
    ["Queued", "Queued", stats?.queued],
    ["Sent", "Sent", stats?.sent],
    ["Delivered", "Delivered", stats?.delivered],
    ["Read", "Read", stats?.read],
    ["Failed", "Failed", stats?.failed],
    ["Skipped", "Skipped", stats?.skipped],
  ];

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      {/* Header */}
      <div className="flex flex-col gap-3">
        <Link
          href="/campaigns"
          className="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" />
          Campaigns
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-bold tracking-tight">
            {campaign?.name ?? "Campaign"}
          </h1>
          {campaign && (
            <Badge variant={CAMPAIGN_STATUS_VARIANT[campaign.status]}>
              {campaign.status}
            </Badge>
          )}
        </div>
        {campaign && (
          <p className="text-sm text-muted-foreground">
            Template <span className="font-medium">{campaign.templateName}</span>
          </p>
        )}
      </div>

      {/* Progress + stats */}
      {stats && (
        <div className="rounded-2xl border p-5">
          <div className="mb-2 flex items-center justify-between text-sm">
            <span className="flex items-center gap-2 font-medium">
              {isLive ? (
                <>
                  <Spinner className="size-3.5" />
                  Sending…
                </>
              ) : (
                "Completed"
              )}
            </span>
            <span className="text-muted-foreground">
              {processed.toLocaleString()} of {total.toLocaleString()} ({percent}%)
            </span>
          </div>
          <Progress value={percent} />

          <div className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
            {(
              [
                ["Total", stats.totalRecipients],
                ["Queued", stats.queued],
                ["Sent", stats.sent],
                ["Delivered", stats.delivered],
                ["Read", stats.read],
                ["Failed", stats.failed],
                ["Skipped", stats.skipped],
              ] as [string, number][]
            ).map(([label, count]) => (
              <div key={label} className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">{label}</div>
                <div className="mt-1 text-xl font-bold">{count.toLocaleString()}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Recipient list */}
      <div className="flex flex-col gap-4">
        <Tabs value={statusFilter ?? "all"} onValueChange={changeFilter}>
          <TabsList className="flex-wrap">
            {TABS.map(([value, label, count]) => (
              <TabsTrigger key={value} value={value}>
                {label}
                {count !== undefined && (
                  <span className="ml-1 text-muted-foreground">{count}</span>
                )}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>

        <div className="rounded-2xl border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Contact</TableHead>
                <TableHead>Phone</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Reason</TableHead>
                <TableHead>Added</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {recipientsLoading && rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="h-24 text-center">
                    <Spinner className="mx-auto size-5" />
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="h-24 text-center text-muted-foreground">
                    No recipients{statusFilter ? ` with status “${statusFilter}”` : ""}.
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="font-medium">{r.contactName}</TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {r.contactPhone}
                    </TableCell>
                    <TableCell>
                      <Badge variant={RECIPIENT_STATUS_VARIANT[r.status] ?? "secondary"}>
                        {r.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {errorLabel(r.errorCode)}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {dateTimeFmt.format(new Date(r.createdAt))}
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </div>

        {/* Pagination */}
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            Page {page} of {totalPages}
            {recipients ? ` · ${recipients.pagination.total.toLocaleString()} total` : ""}
          </span>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              <ChevronLeft className="h-4 w-4" />
              Prev
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
