"use client";

import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

interface DashboardHeroProps {
  name: string;
  role: string;
  email: string;
  /** One-line, role-aware subtitle shown under the greeting. */
  subtitle: string;
}

const ROLE_LABEL: Record<string, string> = {
  SuperAdmin: "Platform Owner",
  CompanyAdmin: "Company Admin",
  Agent: "Agent",
};

function initials(name: string, email: string): string {
  const source = name?.trim() || email || "";
  const parts = source.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return (source.slice(0, 2) || "··").toUpperCase();
}

/**
 * Dashboard hero — keeps the app-wide header shell (rounded-2xl border bg-muted/40)
 * but layers in depth (gradient blooms + dotted grid), a time-aware greeting, an
 * identity chip, and a staggered entrance. Greeting/date resolve after mount so the
 * server and first client render match (no hydration mismatch).
 */
export function DashboardHero({ name, role, email, subtitle }: DashboardHeroProps) {
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => setNow(new Date()), []);

  const first = (name?.trim().split(/\s+/)[0]) || "there";
  const hour = now?.getHours();
  const greeting =
    hour === undefined
      ? "Welcome back"
      : hour < 12
        ? "Good morning"
        : hour < 18
          ? "Good afternoon"
          : "Good evening";
  const dateLabel = now
    ? now.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" })
    : "";

  return (
    <section
      className={cn(
        "relative overflow-hidden rounded-2xl border bg-muted/40 px-6 py-8 sm:px-8 sm:py-10",
        "animate-in fade-in slide-in-from-bottom-2 duration-700",
      )}
    >
      {/* Atmosphere — green blooms + a fading dotted grid, all from theme tokens. */}
      <div aria-hidden className="pointer-events-none absolute inset-0">
        <div className="absolute -right-20 -top-28 h-80 w-80 rounded-full bg-primary/15 blur-3xl" />
        <div className="absolute right-40 top-10 h-44 w-44 rounded-full bg-primary/10 blur-2xl" />
        <div className="absolute -bottom-24 left-1/3 h-56 w-56 rounded-full bg-primary/5 blur-3xl" />
        <div
          className="absolute inset-0 opacity-60 [background-image:radial-gradient(var(--border)_1px,transparent_1px)] [background-size:20px_20px] [mask-image:linear-gradient(120deg,black,transparent_55%)]"
        />
      </div>

      <div className="relative flex flex-col gap-6 sm:flex-row sm:items-center sm:justify-between">
        <div className="space-y-3">
          <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground animate-in fade-in duration-700">
            <span className="inline-block h-1.5 w-1.5 rounded-full bg-primary" />
            Dashboard
          </div>

          <h1
            className="text-3xl font-bold tracking-tight sm:text-4xl animate-in fade-in slide-in-from-bottom-2 duration-700"
            style={{ animationDelay: "80ms", animationFillMode: "backwards" }}
          >
            <span suppressHydrationWarning>{greeting}</span>,{" "}
            <span className="text-primary">{first}</span>
          </h1>

          <p
            className="max-w-xl text-sm text-muted-foreground animate-in fade-in slide-in-from-bottom-2 duration-700"
            style={{ animationDelay: "160ms", animationFillMode: "backwards" }}
          >
            {subtitle}
          </p>

          <div
            className="flex flex-wrap items-center gap-2 pt-1 animate-in fade-in duration-700"
            style={{ animationDelay: "240ms", animationFillMode: "backwards" }}
          >
            <Badge variant="secondary" className="font-medium">
              {ROLE_LABEL[role] ?? role}
            </Badge>
            {dateLabel && (
              <span suppressHydrationWarning className="text-xs text-muted-foreground">
                {dateLabel}
              </span>
            )}
          </div>
        </div>

        {/* Identity medallion */}
        <div
          className="flex items-center gap-3 animate-in fade-in slide-in-from-right-2 duration-700 sm:flex-col sm:items-end sm:text-right"
          style={{ animationDelay: "200ms", animationFillMode: "backwards" }}
        >
          <div className="relative">
            <div className="absolute inset-0 rounded-2xl bg-primary/20 blur-md" aria-hidden />
            <div className="relative flex h-14 w-14 items-center justify-center rounded-2xl border border-primary/30 bg-primary/10 text-lg font-semibold text-primary">
              {initials(name, email)}
            </div>
          </div>
          <div className="min-w-0">
            <p className="truncate text-sm font-medium">{name || email}</p>
            <p className="truncate text-xs text-muted-foreground">{email}</p>
          </div>
        </div>
      </div>
    </section>
  );
}
