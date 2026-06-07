import { type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";

interface StatCardProps {
  label: string;
  value: string | number | null;
  hint?: string;
  icon: LucideIcon;
  index?: number;
}

/**
 * A platform metric tile (super-admin dashboard). Presentational; `value` may be null when
 * the underlying fetch failed, in which case it renders a muted dash.
 */
export function StatCard({ label, value, hint, icon: Icon, index = 0 }: StatCardProps) {
  const display =
    value === null || value === undefined
      ? "—"
      : typeof value === "number"
        ? value.toLocaleString()
        : value;

  return (
    <div
      className={cn(
        "relative overflow-hidden rounded-xl border bg-card p-5",
        "animate-in fade-in slide-in-from-bottom-2 duration-500",
      )}
      style={{ animationDelay: `${120 + index * 70}ms`, animationFillMode: "backwards" }}
    >
      <div
        aria-hidden
        className="pointer-events-none absolute -right-8 -top-10 h-24 w-24 rounded-full bg-primary/5 blur-2xl"
      />
      <div className="relative flex items-center justify-between">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
        <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Icon className="h-4 w-4" />
        </div>
      </div>
      <p className="relative mt-3 text-3xl font-bold tracking-tight tabular-nums">{display}</p>
      {hint && <p className="relative mt-1 text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
