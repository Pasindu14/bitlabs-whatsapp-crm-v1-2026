import type { ReactNode } from "react";
import { DoubleTick } from "@/components/brand-mark";
import { cn } from "@/lib/utils";

export interface HeroFact {
  label: string;
  /** Pre-formatted. Null renders a dash (the count couldn't load). */
  value: string | number | null;
  /** Tints the dot beside the label so a status count reads at a glance. */
  tone?: "green" | "amber" | "red" | "blue" | "plain";
}

const DOT: Record<NonNullable<HeroFact["tone"]>, string> = {
  green: "bg-[#dcf8c6]",
  amber: "bg-[#ffd27a]",
  red: "bg-[#ff9f80]",
  blue: "bg-[#7cc8f2]",
  plain: "bg-white/40",
};

/**
 * The page title band used across the app: leaf green, the brand's double tick in the corner, the
 * page's purpose in one line, and — where the page has them — the few live numbers that matter there.
 * Presentational only, so server pages can pass counts they fetched themselves.
 */
export function PageHero({
  title,
  description,
  facts,
  children,
  className,
}: {
  title: string;
  description: ReactNode;
  facts?: HeroFact[];
  /** Actions, shown at the right on wide screens. */
  children?: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={cn(
        "relative overflow-hidden rounded-2xl bg-[#0f6b3e] px-6 py-6 text-[#f2faee] sm:px-8 dark:bg-[#0b4f2e]",
        className,
      )}
    >
      <DoubleTick className="pointer-events-none absolute -right-10 -bottom-24 w-[22rem] text-[#0b5a33]" />

      <div className="relative flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          <h1 className="text-[1.75rem] leading-tight font-light tracking-[-0.03em]">{title}</h1>
          <p className="mt-1.5 max-w-2xl text-[14px] leading-relaxed text-[#b9dcc5]">{description}</p>

          {facts && facts.length > 0 && (
            <dl className="mt-5 flex flex-wrap gap-x-9 gap-y-4">
              {facts.map((f) => (
                <div key={f.label}>
                  <dt className="flex items-center gap-1.5 text-[12.5px] text-[#b9dcc5]">
                    {f.tone && <span className={cn("size-1.5 rounded-full", DOT[f.tone])} />}
                    {f.label}
                  </dt>
                  <dd className="mt-0.5 text-[1.75rem] leading-none font-light tracking-[-0.02em] tabular-nums">
                    {f.value === null ? "–" : typeof f.value === "number" ? f.value.toLocaleString("en-US") : f.value}
                  </dd>
                </div>
              ))}
            </dl>
          )}
        </div>

        {children ? <div className="flex shrink-0 flex-wrap gap-2">{children}</div> : null}
      </div>
    </section>
  );
}

/** Total from a paginated server-action result, or null when it failed. */
export function totalOf(r: { success: boolean; data?: { pagination?: { total: number } } }): number | null {
  return r.success && r.data?.pagination ? r.data.pagination.total : null;
}
