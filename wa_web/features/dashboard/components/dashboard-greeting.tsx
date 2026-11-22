"use client";

import { useEffect, useState } from "react";

interface DashboardGreetingProps {
  name: string;
  subtitle: string;
  /** Header actions (buttons), shown on the right. */
  children?: React.ReactNode;
}

/**
 * Page title row: time-aware greeting, today's date and the page's main actions.
 * Greeting and date resolve after mount so the server and first client render match.
 */
export function DashboardGreeting({ name, subtitle, children }: DashboardGreetingProps) {
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => setNow(new Date()), []);

  const first = name?.trim().split(/\s+/)[0] || "there";
  const hour = now?.getHours();
  const greeting =
    hour === undefined ? "Welcome back" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
  const date = now?.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" });

  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="space-y-1">
        <p className="text-xs font-medium text-muted-foreground" suppressHydrationWarning>
          {date ?? " "}
        </p>
        <h1 className="text-2xl font-semibold tracking-tight md:text-3xl" suppressHydrationWarning>
          {greeting}, {first}
        </h1>
        <p className="text-sm text-muted-foreground">{subtitle}</p>
      </div>
      {children && <div className="flex flex-wrap gap-2">{children}</div>}
    </div>
  );
}
