import type { ComponentType } from "react";
import {
  CheckCheck,
  Clock,
  Eye,
  Inbox,
  Send,
  SkipForward,
  TrendingUp,
  Users,
  XCircle,
} from "lucide-react";

/** Accent colour + icon for each delivery-funnel stat, shared by the campaign detail page and the
 *  quick-stats dialog so both read as one system. Colours trace queued → accepted → sent →
 *  delivered → read, with red/slate for the failure/skip off-ramps. Full literal class strings so
 *  Tailwind's JIT keeps them. */
export const STAT_META: Record<
  string,
  { icon: ComponentType<{ className?: string }>; pill: string; value?: string }
> = {
  Total: {
    icon: Users,
    pill: "bg-slate-500/10 text-slate-600 dark:bg-slate-400/10 dark:text-slate-300",
  },
  Queued: {
    icon: Clock,
    pill: "bg-amber-500/10 text-amber-600 dark:bg-amber-400/10 dark:text-amber-400",
  },
  Accepted: {
    icon: Inbox,
    pill: "bg-sky-500/10 text-sky-600 dark:bg-sky-400/10 dark:text-sky-400",
  },
  Sent: {
    icon: Send,
    pill: "bg-violet-500/10 text-violet-600 dark:bg-violet-400/10 dark:text-violet-400",
  },
  Delivered: {
    icon: CheckCheck,
    pill: "bg-cyan-500/10 text-cyan-600 dark:bg-cyan-400/10 dark:text-cyan-400",
  },
  Read: {
    icon: Eye,
    pill: "bg-emerald-500/10 text-emerald-600 dark:bg-emerald-400/10 dark:text-emerald-400",
    value: "text-emerald-600 dark:text-emerald-400",
  },
  Failed: {
    icon: XCircle,
    pill: "bg-rose-500/10 text-rose-600 dark:bg-rose-400/10 dark:text-rose-400",
    value: "text-rose-600 dark:text-rose-400",
  },
  Skipped: {
    icon: SkipForward,
    pill: "bg-slate-500/10 text-slate-500 dark:bg-slate-400/10 dark:text-slate-400",
  },
  "Delivery rate": {
    icon: CheckCheck,
    pill: "bg-cyan-500/10 text-cyan-600 dark:bg-cyan-400/10 dark:text-cyan-400",
  },
  "Read rate": {
    icon: TrendingUp,
    pill: "bg-emerald-500/10 text-emerald-600 dark:bg-emerald-400/10 dark:text-emerald-400",
    value: "text-emerald-600 dark:text-emerald-400",
  },
};
