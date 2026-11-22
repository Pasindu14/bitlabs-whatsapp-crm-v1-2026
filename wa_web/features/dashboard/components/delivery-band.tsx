import { TriangleAlert } from "lucide-react";
import { DoubleTick } from "@/components/brand-mark";
import { cn } from "@/lib/utils";
import { pct } from "@/features/dashboard/format";

/** Single tick in the brand's stroke style. */
function SingleTick({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 17 12" className={className} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="M3 6.5 6.2 10 12 2.5" />
    </svg>
  );
}

interface DeliveryBandProps {
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  /** Week-over-week change in messages sent, or null when there is no baseline. */
  trend: number | null;
  /** Heading; defaults to the dashboard's 30-day wording. */
  title?: string;
  /** Hint under the failed count when something failed. */
  failedHint?: string;
}

const fmt = (n: number) => n.toLocaleString("en-US");

/**
 * The dashboard's hero: the last 30 days told as WhatsApp's own receipts — one tick, two ticks,
 * blue ticks — with one bar showing where every message ended up.
 */
export function DeliveryBand({
  sent,
  delivered,
  read,
  failed,
  trend,
  title = "Your messages, last 30 days",
  failedHint = "Open Analytics to see why",
}: DeliveryBandProps) {
  // Segments of the bar. "Delivered" counts messages delivered but not (yet) read, so the four add up.
  // The API reports failed messages outside the sent total, so the bar is out of everything attempted.
  const readN = Math.min(read, sent);
  const deliveredOnly = Math.max(0, Math.min(delivered, sent) - readN);
  const attempted = Math.max(sent, readN + deliveredOnly) + failed;
  const pending = Math.max(0, attempted - readN - deliveredOnly - failed);
  const failedN = failed;
  const share = (n: number) => (attempted > 0 ? `${(n / attempted) * 100}%` : "0%");

  return (
    <section
      aria-label={title}
      className="relative overflow-hidden rounded-2xl bg-[#0f6b3e] p-6 text-[#f2faee] sm:p-8"
    >
      {/* The brand tick, huge and tone-on-tone, as on the sign-in page. */}
      <DoubleTick className="pointer-events-none absolute -right-10 -bottom-24 w-[26rem] text-[#0b5a33]" />

      <div className="relative flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-[15px] font-medium">{title}</h2>
        {trend !== null && (
          <p className="text-[13px] text-[#b9dcc5]">
            {trend >= 0 ? "Up" : "Down"} {Math.abs(trend).toFixed(0)}% on the week before
          </p>
        )}
      </div>

      <div className="relative mt-6 grid grid-cols-2 gap-x-6 gap-y-7 lg:grid-cols-4">
        <Stage icon={<SingleTick className="w-5 text-[#b9dcc5]" />} label="Sent" value={fmt(sent)} note="Every message that left your numbers" />
        <Stage
          icon={<DoubleTick className="w-5 text-[#b9dcc5]" />}
          label="Delivered"
          value={fmt(delivered)}
          note={`${pct(delivered, sent) ?? 0}% reached the phone`}
        />
        <Stage
          icon={<DoubleTick className="w-5 text-[#7cc8f2]" />}
          label="Read"
          value={fmt(read)}
          note={`${pct(read, sent) ?? 0}% were opened`}
        />
        <Stage
          icon={<TriangleAlert className="size-[18px] text-[#ffc9b8]" />}
          label="Failed"
          value={fmt(failed)}
          note={failed > 0 ? failedHint : "Nothing failed"}
        />
      </div>

      {/* Where every message ended up. */}
      <div className="relative mt-7">
        <div className="flex h-2.5 w-full overflow-hidden rounded-full bg-white/12" role="img" aria-label={`${readN} read, ${deliveredOnly} delivered, ${pending} on the way, ${failedN} failed`}>
          <span className="h-full bg-[#7cc8f2]" style={{ width: share(readN) }} />
          <span className="h-full bg-[#b9dcc5]" style={{ width: share(deliveredOnly) }} />
          <span className="h-full bg-white/35" style={{ width: share(pending) }} />
          <span className="h-full bg-[#ff9f80]" style={{ width: share(failedN) }} />
        </div>
        <div className="mt-2.5 flex flex-wrap gap-x-5 gap-y-1 text-[12px] text-[#b9dcc5]">
          <Key className="bg-[#7cc8f2]" label="Read" />
          <Key className="bg-[#b9dcc5]" label="Delivered, not read yet" />
          <Key className="bg-white/35" label="On the way" />
          <Key className="bg-[#ff9f80]" label="Failed" />
        </div>
      </div>
    </section>
  );
}

function Stage({ icon, label, value, note }: { icon: React.ReactNode; label: string; value: string; note: string }) {
  return (
    <div className="min-w-0">
      <p className="flex items-center gap-2 text-[13px] text-[#b9dcc5]">
        {icon}
        {label}
      </p>
      <p className="mt-1.5 text-[clamp(2.25rem,3.6vw,3rem)] leading-none font-light tracking-[-0.03em] tabular-nums">{value}</p>
      <p className="mt-2 text-[12px] text-[#b9dcc5]">{note}</p>
    </div>
  );
}

function Key({ className, label }: { className: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={cn("size-2 rounded-full", className)} />
      {label}
    </span>
  );
}
