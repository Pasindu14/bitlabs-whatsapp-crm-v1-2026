import { cn } from "@/lib/utils";
import type { TemplateStatus } from "@/features/templates/types";

const STATUS_STYLES: Record<TemplateStatus, string> = {
  Draft: "bg-muted text-muted-foreground",
  Pending: "bg-amber-500/15 text-amber-600 dark:text-amber-400",
  Approved: "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400",
  Rejected: "bg-red-500/15 text-red-600 dark:text-red-400",
  Paused: "bg-orange-500/15 text-orange-600 dark:text-orange-400",
  Disabled: "bg-zinc-500/15 text-zinc-600 dark:text-zinc-400",
};

export function TemplateStatusBadge({ status, className }: { status: TemplateStatus; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium",
        STATUS_STYLES[status],
        className
      )}
    >
      {status}
    </span>
  );
}
