"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft, ChevronLeft, ChevronRight, Info } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { Spinner } from "@/components/ui/spinner";
import {
  Popover,
  PopoverContent,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@/components/ui/popover";
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

// Human labels for the engine's own skip/fail codes AND the Meta Cloud API policy codes we know
// about (mirrors wa_api MetaPolicyErrorCodes). Anything unmapped falls back to the raw code.
// https://developers.facebook.com/docs/whatsapp/cloud-api/support/error-codes
const ERROR_LABELS: Record<string, string> = {
  // Engine (our own) codes
  NOT_ON_WHATSAPP: "Not on WhatsApp",
  OPT_OUT: "Opted out",
  NO_CONSENT: "No consent",
  SEND_FAILED: "Send failed",
  JOB_ERROR: "Internal error",
  CAMPAIGN_FAILED: "Campaign failed",
  HEADER_MEDIA_MISSING: "Template media missing",
  // Meta Cloud API codes
  "131049": "Not delivered — WhatsApp frequency cap",
  "131048": "Blocked — spam rate limit",
  "131026": "Undeliverable — not a WhatsApp user",
  "131031": "Account suspended by Meta",
  "368": "Account restricted by Meta",
  "130429": "Rate limited — will retry",
  "131056": "Rate limited to this number — will retry",
};

// Plain-language explanation shown in the info popover, so a non-technical user understands what
// happened and what (if anything) they should do — instead of a bare code.
type ErrorDetail = { meaning: string; action: string };
const ERROR_DETAILS: Record<string, ErrorDetail> = {
  "131049": {
    meaning:
      "WhatsApp didn't deliver this marketing message because the recipient has already hit their limit of marketing messages recently. WhatsApp sets this limit per person — counting messages from all businesses, not just yours — to reduce spam.",
    action:
      "Nothing is wrong with your number, and you were not charged for it. Try again in a day or two, and avoid sending this contact many marketing messages close together.",
  },
  "131048": {
    meaning:
      "WhatsApp blocked this message because your number's recent sending was flagged as spam-like. This is a quality signal on your account.",
    action:
      "Slow down, message only contacts who expect to hear from you, and review your template content before resending.",
  },
  "131026": {
    meaning:
      "This number can't receive the message — it isn't a WhatsApp user, or the number is invalid.",
    action:
      "No action needed. The contact is automatically marked so future campaigns skip it.",
  },
  "131031": {
    meaning: "Meta has suspended your WhatsApp Business account, so no messages can be sent.",
    action:
      "Open Meta Business Suite / WhatsApp Manager to see the reason and resolve the suspension. Sending stays blocked until it's lifted.",
  },
  "368": {
    meaning: "Meta has placed a temporary restriction on your WhatsApp account.",
    action:
      "Check WhatsApp Manager for the restriction details. Sending resumes once Meta lifts it.",
  },
  "130429": {
    meaning: "Messages were sent too fast and WhatsApp asked us to slow down. The message itself wasn't rejected.",
    action: "No action needed — the system automatically backs off and retries.",
  },
  "131056": {
    meaning:
      "Too many messages were sent to this one number in a short time, so WhatsApp throttled it. The message wasn't rejected.",
    action: "No action needed — the system automatically retries after a short delay.",
  },
  // Engine (our own) reasons
  NO_CONSENT: {
    meaning:
      "This contact has no recorded opt-in, and the campaign's consent override was off, so we didn't send to them.",
    action:
      "Collect opt-in from the contact, or enable the consent override on the campaign if you have proof of consent on file.",
  },
  OPT_OUT: {
    meaning: "This contact has opted out of your messages, so they are never sent to.",
    action: "No action needed — opted-out contacts are always skipped to keep you compliant.",
  },
  NOT_ON_WHATSAPP: {
    meaning: "This number isn't reachable on WhatsApp.",
    action: "No action needed — the contact is marked so future campaigns skip it.",
  },
  HEADER_MEDIA_MISSING: {
    meaning:
      "The template's header image/video/document couldn't be attached at send time, so the message failed.",
    action: "Re-upload the header media on the template, then resend.",
  },
  SEND_FAILED: {
    meaning: "WhatsApp rejected the send for a reason we couldn't map to a specific cause.",
    action: "Try resending. If it keeps failing, check the template and the contact's number.",
  },
  JOB_ERROR: {
    meaning: "An unexpected internal error happened while sending to this contact.",
    action: "Try resending. If it persists, contact support with the campaign link.",
  },
  CAMPAIGN_FAILED: {
    meaning: "The campaign was stopped before this contact could be sent to.",
    action: "Review why the campaign stopped, then relaunch.",
  },
};

function errorLabel(code: string | null): string {
  if (!code) return "—";
  if (ERROR_LABELS[code]) return ERROR_LABELS[code];
  // Numeric = an unmapped raw Meta code; non-numeric = an unmapped engine code (show as-is).
  return /^\d+$/.test(code) ? `WhatsApp error ${code}` : code;
}

function errorDetail(code: string | null): ErrorDetail {
  if (code && ERROR_DETAILS[code]) return ERROR_DETAILS[code];
  return {
    meaning: /^\d+$/.test(code ?? "")
      ? `WhatsApp reported error code ${code} for this contact.`
      : "This message couldn't be delivered to this contact.",
    action: "Try resending. If it keeps failing, contact support and share this campaign link.",
  };
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
          {campaign?.overrideConsentGate && (
            <Badge variant="destructive" title="This campaign sends to contacts without recorded opt-in">
              Consent override
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

          {stats.sentWithoutConsent > 0 && (
            <div className="mt-4 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
              ⚠ {stats.sentWithoutConsent.toLocaleString()} message
              {stats.sentWithoutConsent !== 1 ? "s" : ""} sent to contacts without recorded opt-in
              (consent override).
            </div>
          )}
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
                      {r.errorCode ? (
                        // An actual delivery/send failure — show the real reason plus a plain-language
                        // explanation the user can read, instead of a bare code.
                        <div className="flex items-center gap-1.5">
                          <span className="text-destructive">{errorLabel(r.errorCode)}</span>
                          <Popover>
                            <PopoverTrigger asChild>
                              <button
                                type="button"
                                className="text-muted-foreground/70 transition-colors hover:text-foreground"
                                aria-label="Why did this fail?"
                              >
                                <Info className="size-3.5" />
                              </button>
                            </PopoverTrigger>
                            <PopoverContent align="start" className="w-80 gap-3">
                              <PopoverHeader>
                                <PopoverTitle>{errorLabel(r.errorCode)}</PopoverTitle>
                              </PopoverHeader>
                              <div className="space-y-3">
                                <div className="space-y-1">
                                  <p className="text-xs font-medium text-foreground">Why this happened</p>
                                  <p className="text-sm text-muted-foreground">
                                    {errorDetail(r.errorCode).meaning}
                                  </p>
                                </div>
                                <div className="space-y-1">
                                  <p className="text-xs font-medium text-foreground">What to do</p>
                                  <p className="text-sm text-muted-foreground">
                                    {errorDetail(r.errorCode).action}
                                  </p>
                                </div>
                              </div>
                              <p className="text-[11px] text-muted-foreground/60">
                                {/^\d+$/.test(r.errorCode) ? `WhatsApp error code ${r.errorCode}` : `Code: ${r.errorCode}`}
                              </p>
                            </PopoverContent>
                          </Popover>
                        </div>
                      ) : r.sentWithoutConsent ? (
                        // Sent successfully, but without recorded opt-in — audit annotation only.
                        <span className="text-amber-600 dark:text-amber-500" title="Sent despite no recorded opt-in">
                          No consent (sent)
                        </span>
                      ) : (
                        "—"
                      )}
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
