"use client";

import Link from "next/link";
import { ArrowLeft, TriangleAlert } from "lucide-react";
import { DoubleTick } from "@/components/brand-mark";
import type { Campaign, CampaignStats } from "@/features/campaigns/types";

/** Single tick in the brand's stroke style. */
function SingleTick({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 17 12" className={className} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="M3 6.5 6.2 10 12 2.5" />
    </svg>
  );
}

interface AudienceHealth {
  total: number;
  sendable: number;
  optedOut: number;
  invalid: number;
}

const num = (n: number) => n.toLocaleString("en-US");
const when = new Intl.DateTimeFormat("en-US", { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });
const pct = (part: number, total: number) => (total > 0 ? Math.round((part / total) * 100) : 0);

/** Pill colour per campaign status, readable on the green band. */
const STATUS_PILL: Record<string, string> = {
  Running: "bg-[#dcf8c6] text-[#0f6b3e]",
  Completed: "bg-[#f2faee] text-[#0f6b3e]",
  Scheduled: "bg-[#d6ecfb] text-[#1b5e9e]",
  Draft: "bg-white/15 text-[#f2faee]",
  Paused: "bg-[#ffe7b3] text-[#7a4b00]",
  Failed: "bg-[#ffd5c8] text-[#9e2b1b]",
  Cancelled: "bg-white/15 text-[#f2faee]",
};

/**
 * Campaign detail header: what was sent, to whom, and how far it got — told with WhatsApp's own
 * receipts, as on the dashboard. Recipient statuses are exclusive buckets, so each receipt counts
 * everyone who got at least that far (read implies delivered implies sent).
 */
export function CampaignHero({
  campaign,
  stats,
  health,
  isLive,
}: {
  campaign?: Campaign;
  stats?: CampaignStats;
  health?: AudienceHealth;
  isLive: boolean;
}) {
  const total = stats?.totalRecipients ?? 0;
  const read = stats?.read ?? 0;
  const delivered = (stats?.delivered ?? 0) + read;
  const sent = (stats?.sent ?? 0) + delivered;
  const failed = stats?.failed ?? 0;
  const processed = stats ? total - stats.queued : 0;
  const audience =
    campaign && campaign.contactListNames.length > 0
      ? campaign.contactListNames.join(", ")
      : campaign
        ? `${num(campaign.contactIds.length)} chosen contact${campaign.contactIds.length === 1 ? "" : "s"}`
        : null;
  const started = campaign?.launchedAt ?? campaign?.scheduledAt ?? null;

  return (
    <section className="relative overflow-hidden rounded-2xl bg-[#0f6b3e] px-6 py-6 text-[#f2faee] sm:px-8 dark:bg-[#0b4f2e]">
      <DoubleTick className="pointer-events-none absolute -right-10 -bottom-28 w-[24rem] text-[#0b5a33]" />

      <div className="relative">
        <Link
          href="/campaigns"
          className="inline-flex items-center gap-1.5 rounded-md text-[13px] text-[#b9dcc5] hover:text-[#f2faee] focus-visible:ring-2 focus-visible:ring-white/50 focus-visible:outline-none"
        >
          <ArrowLeft className="size-3.5" /> All campaigns
        </Link>

        <div className="mt-3 flex flex-wrap items-center gap-3">
          <h1 className="min-w-0 text-[2rem] leading-tight font-light tracking-[-0.03em] break-words">
            {campaign?.name ?? "Campaign"}
          </h1>
          {campaign && (
            <span className={`flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[12px] font-medium ${STATUS_PILL[campaign.status] ?? STATUS_PILL.Draft}`}>
              {isLive && <span className="size-1.5 animate-pulse rounded-full bg-current motion-reduce:animate-none" />}
              {isLive ? "Sending" : campaign.status}
            </span>
          )}
        </div>

        {campaign && (
          <dl className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-[13px]">
            <Meta label="Template" value={campaign.templateName} />
            {audience && <Meta label="Sent to" value={audience} />}
            {started && <Meta label={campaign.launchedAt ? "Started" : "Starts"} value={when.format(new Date(started))} />}
          </dl>
        )}

        {stats && (
          <>
            <div className="mt-6 grid grid-cols-2 gap-x-6 gap-y-5 lg:grid-cols-4">
              <Stage icon={<SingleTick className="w-5 text-[#b9dcc5]" />} label="Sent" value={sent} note={`${pct(sent, total)}% of ${num(total)}`} />
              <Stage icon={<DoubleTick className="w-5 text-[#b9dcc5]" />} label="Delivered" value={delivered} note={`${pct(delivered, total)}% reached the phone`} />
              <Stage icon={<DoubleTick className="w-5 text-[#7cc8f2]" />} label="Read" value={read} note={`${pct(read, total)}% were opened`} />
              <Stage
                icon={<TriangleAlert className="size-[18px] text-[#ffc9b8]" />}
                label="Failed"
                value={failed}
                note={failed > 0 ? "Filter below to see why" : "Nothing failed"}
              />
            </div>

            {isLive && (
              <div className="mt-5">
                <div className="h-1.5 w-full overflow-hidden rounded-full bg-white/12">
                  <div className="h-full rounded-full bg-[#dcf8c6] transition-[width] duration-700" style={{ width: `${pct(processed, total)}%` }} />
                </div>
                <p className="mt-1.5 text-[12px] text-[#b9dcc5] tabular-nums">
                  Sending: {num(processed)} of {num(total)} handed to WhatsApp
                </p>
              </div>
            )}
          </>
        )}

        {health && health.total > 0 && (
          <p className="mt-5 flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-white/12 pt-4 text-[12.5px] text-[#b9dcc5]">
            <span className="text-[#f2faee]">{num(health.total)} contacts targeted</span>
            <Dot className="bg-[#dcf8c6]" label={`${num(health.sendable)} could be messaged`} />
            <Dot className="bg-white/40" label={`${num(health.optedOut)} opted out`} />
            <Dot className="bg-[#ff9f80]" label={`${num(health.invalid)} not on WhatsApp`} />
          </p>
        )}
      </div>
    </section>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex gap-1.5">
      <dt className="text-[#b9dcc5]">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}

function Stage({ icon, label, value, note }: { icon: React.ReactNode; label: string; value: number; note: string }) {
  return (
    <div>
      <p className="flex items-center gap-2 text-[13px] text-[#b9dcc5]">
        {icon}
        {label}
      </p>
      <p className="mt-1 text-[2.25rem] leading-none font-light tracking-[-0.03em] tabular-nums">{num(value)}</p>
      <p className="mt-1.5 text-[12px] text-[#b9dcc5]">{note}</p>
    </div>
  );
}

function Dot({ className, label }: { className: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={`size-1.5 rounded-full ${className}`} />
      {label}
    </span>
  );
}
