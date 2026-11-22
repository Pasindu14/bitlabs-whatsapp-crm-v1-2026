"use client";

import { formatDistanceToNow } from "date-fns";
import { WifiOff, Phone, ShieldCheck, ShieldX, Clock } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { useConnections } from "@/features/connections/hooks/use-connections";
import type {
  MessagingTier,
  MyWabaConnection,
  WabaConnectionStatus,
} from "@/features/connections/types";

// Messaging tier → daily unique-recipient cap as a number (null = unlimited).
const TIER_CAP: Record<MessagingTier, number | null> = {
  Tier250: 250,
  Tier1K: 1000,
  Tier10K: 10000,
  Tier100K: 100000,
  Unlimited: null,
};

/** "94751803295" → "+94 75 180 3295". Sri Lanka and UAE get local grouping; others just get the plus. */
function formatPhone(raw: string): string {
  const d = raw.replace(/\D/g, "");
  if (d.startsWith("94") && d.length === 11) return `+94 ${d.slice(2, 4)} ${d.slice(4, 7)} ${d.slice(7)}`;
  if (d.startsWith("971") && d.length === 12) return `+971 ${d.slice(3, 5)} ${d.slice(5, 8)} ${d.slice(8)}`;
  return raw.startsWith("+") ? raw : `+${d}`;
}

const STATUS_COPY: Record<WabaConnectionStatus, { label: string; text: string; dot: string; pill: string }> = {
  Connected: {
    label: "Working",
    text: "This number can send and receive messages.",
    dot: "bg-[#1fbf5c]",
    pill: "bg-[#e3f1e8] text-[#0f6b3e]",
  },
  Invalid: {
    label: "Needs attention",
    text: "Meta rejected the access token, so nothing can be sent. Ask your platform administrator for a new one.",
    dot: "bg-[#e5533d]",
    pill: "bg-red-50 text-red-700",
  },
  Disconnected: {
    label: "Disconnected",
    text: "This number is switched off in Growchat. Ask your platform administrator to reconnect it.",
    dot: "bg-slate-400",
    pill: "bg-muted text-muted-foreground",
  },
};

// Meta quality rating → how many of the three steps are lit, and what it means in plain words.
const QUALITY: Record<string, { steps: number; label: string; text: string; color: string }> = {
  GREEN: { steps: 3, label: "High", text: "Customers are happy with your messages.", color: "bg-[#1fbf5c]" },
  YELLOW: { steps: 2, label: "Medium", text: "Some customers block or report you. Send fewer, more useful messages.", color: "bg-amber-400" },
  RED: { steps: 1, label: "Low", text: "Many blocks or reports. Meta may limit this number soon.", color: "bg-[#e5533d]" },
};

function HealthBlock({
  title,
  value,
  children,
  text,
}: {
  title: string;
  value: React.ReactNode;
  children?: React.ReactNode;
  text: string;
}) {
  return (
    <div className="flex flex-col gap-2 rounded-xl bg-[#f4f8f5] p-4 dark:bg-muted/40">
      <p className="text-[12.5px] text-muted-foreground">{title}</p>
      <div className="text-[1.375rem] leading-none font-light tracking-[-0.02em]">{value}</div>
      {children}
      <p className="mt-auto text-[12.5px] leading-relaxed text-muted-foreground">{text}</p>
    </div>
  );
}

function ConnectionCard({ conn }: { conn: MyWabaConnection }) {
  const status = STATUS_COPY[conn.status];
  const quality = conn.qualityRating ? QUALITY[conn.qualityRating.toUpperCase()] : undefined;
  const cap = TIER_CAP[conn.messagingTier];
  const usedPct = cap ? Math.min(100, Math.round((conn.dailySentToday / cap) * 100)) : 0;
  const bar = usedPct >= 90 ? "bg-[#e5533d]" : usedPct >= 70 ? "bg-amber-400" : "bg-[#1fbf5c]";

  return (
    <article className="overflow-hidden rounded-2xl border bg-card shadow-[0_14px_34px_-26px_rgb(16_60_36/0.5)]">
      <header className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5 sm:px-6">
        <div className="flex items-center gap-3.5">
          <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-[#0f6b3e] text-[#f2faee]">
            <Phone className="size-5" />
          </span>
          <div>
            <p className="text-[1.75rem] leading-none font-light tracking-[-0.02em] tabular-nums">
              {formatPhone(conn.displayPhoneNumber)}
            </p>
            <p className="mt-1 text-[12.5px] text-muted-foreground">WhatsApp business number</p>
          </div>
        </div>
        <span className={cn("flex items-center gap-1.5 rounded-full px-3 py-1 text-[12.5px] font-medium", status.pill)}>
          <span className={cn("size-2 rounded-full", status.dot)} />
          {status.label}
        </span>
      </header>

      <div className="grid gap-3 px-5 py-5 sm:px-6 md:grid-cols-3">
        <HealthBlock
          title="Can send"
          value={conn.status === "Connected" ? "Yes" : "No"}
          text={conn.healthCheckErrorMessage && conn.status !== "Connected" ? conn.healthCheckErrorMessage : status.text}
        />

        <HealthBlock
          title="Message quality"
          value={quality ? quality.label : "Not rated yet"}
          text={quality ? quality.text : "Meta rates a number after it has sent a few messages."}
        >
          <div className="flex gap-1" role="img" aria-label={quality ? `Quality ${quality.label}` : "Quality not rated yet"}>
            {[1, 2, 3].map((step) => (
              <span
                key={step}
                className={cn("h-1.5 flex-1 rounded-full", quality && step <= quality.steps ? quality.color : "bg-border")}
              />
            ))}
          </div>
        </HealthBlock>

        <HealthBlock
          title="New people messaged today"
          value={
            <span className="tabular-nums">
              {conn.dailySentToday.toLocaleString()}
              <span className="text-[14px] text-muted-foreground">
                {cap === null ? " with no daily limit" : ` of ${cap.toLocaleString()}`}
              </span>
            </span>
          }
          text={
            cap === null
              ? "Meta lets this number start as many new chats as it likes."
              : "Meta caps how many new people you can start chats with each day. Good quality raises the cap."
          }
        >
          {cap !== null && (
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-border">
              <div
                className={cn("h-full rounded-full transition-[width] duration-700", bar)}
                style={{ width: `${Math.max(usedPct, 1)}%` }}
              />
            </div>
          )}
        </HealthBlock>
      </div>

      <footer className="flex flex-wrap gap-x-8 gap-y-2 border-t bg-muted/30 px-5 py-3 text-[12.5px] sm:px-6">
        <Detail label="Phone number ID" value={conn.phoneNumberId} />
        <Detail label="Business account ID" value={conn.wabaId} />
        <Detail
          label="Access token"
          value={
            conn.hasAccessToken ? (
              <span className="inline-flex items-center gap-1 text-[#0f6b3e]">
                <ShieldCheck className="size-3.5" /> Saved
              </span>
            ) : (
              <span className="inline-flex items-center gap-1 text-red-600">
                <ShieldX className="size-3.5" /> Missing
              </span>
            )
          }
        />
        {conn.lastHealthCheckAt && (
          <Detail
            label="Last checked"
            value={
              <span className="inline-flex items-center gap-1">
                <Clock className="size-3" />
                {formatDistanceToNow(new Date(conn.lastHealthCheckAt), { addSuffix: true })}
              </span>
            }
          />
        )}
      </footer>
    </article>
  );
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center gap-1.5">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium tabular-nums">{value}</span>
    </div>
  );
}

function ConnectionSkeleton() {
  return (
    <div className="rounded-xl border bg-card p-5 space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <Skeleton className="size-10 rounded-full" />
          <div className="space-y-2">
            <Skeleton className="h-4 w-36" />
            <Skeleton className="h-3 w-28" />
          </div>
        </div>
        <Skeleton className="h-5 w-24 rounded-full" />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Skeleton className="h-8" />
        <Skeleton className="h-8" />
      </div>
    </div>
  );
}

export function ConnectionsView() {
  const { data, isLoading } = useConnections();

  return (
    <div className="space-y-4">
      {isLoading ? (
        <div className="grid gap-4 sm:grid-cols-2">
          {Array.from({ length: 2 }).map((_, i) => (
            <ConnectionSkeleton key={i} />
          ))}
        </div>
      ) : !data?.length ? (
        <div className="flex flex-col items-center justify-center gap-3 rounded-xl border border-dashed py-20 text-center">
          <WifiOff className="size-10 text-muted-foreground/40" />
          <div>
            <p className="text-sm font-medium">No WhatsApp number connected yet</p>
            <p className="mt-1 text-xs text-muted-foreground">
              Ask your platform administrator to connect your business number. Then you can send and receive messages.
            </p>
          </div>
        </div>
      ) : (
        <>
          <div className="grid gap-4">
            {data.map((conn) => (
              <ConnectionCard key={conn.id} conn={conn} />
            ))}
          </div>
        </>
      )}
    </div>
  );
}
