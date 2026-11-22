"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";

interface Props {
  from: string;
  to: string;
  onChange: (range: { from: string; to: string }) => void;
}

const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

/** yyyy-mm-dd range covering the last `days` days, today included. */
export function lastDays(days: number) {
  const to = new Date();
  const from = new Date(to.getFullYear(), to.getMonth(), to.getDate() - (days - 1));
  return { from: iso(from), to: iso(to) };
}

const PRESETS = [7, 30, 90];

/** Which preset (if any) a range matches, so the right chip lights up. */
function presetOf(from: string, to: string): string {
  for (const d of PRESETS) {
    const r = lastDays(d);
    if (r.from === from && r.to === to) return String(d);
  }
  return "custom";
}

/** Quick ranges for the common cases; "Custom" opens two date fields for anything else. */
export function AnalyticsDateRangePicker({ from, to, onChange }: Props) {
  const [mode, setMode] = useState(() => presetOf(from, to));

  return (
    <div className="flex flex-wrap items-center justify-end gap-2">
      <ToggleGroup
        type="single"
        value={mode}
        onValueChange={(v) => {
          if (!v) return;
          setMode(v);
          if (v !== "custom") onChange(lastDays(Number(v)));
        }}
        className="gap-0.5 rounded-lg bg-muted p-0.5"
        aria-label="Date range"
      >
        {PRESETS.map((d) => (
          <ToggleGroupItem
            key={d}
            value={String(d)}
            className="h-7 rounded-md border-0 px-3 text-xs font-normal text-muted-foreground data-[state=on]:bg-card data-[state=on]:font-medium data-[state=on]:text-foreground data-[state=on]:shadow-[0_1px_2px_rgb(0_0_0/0.08)]"
          >
            {d} days
          </ToggleGroupItem>
        ))}
        <ToggleGroupItem
          value="custom"
          className="h-7 rounded-md border-0 px-3 text-xs font-normal text-muted-foreground data-[state=on]:bg-card data-[state=on]:font-medium data-[state=on]:text-foreground data-[state=on]:shadow-[0_1px_2px_rgb(0_0_0/0.08)]"
        >
          Custom
        </ToggleGroupItem>
      </ToggleGroup>

      {mode === "custom" && (
        <div className="flex items-center gap-2 text-xs">
          <Label htmlFor="analytics-from" className="sr-only">
            From
          </Label>
          <Input
            id="analytics-from"
            type="date"
            value={from}
            max={to}
            onChange={(e) => e.target.value && onChange({ from: e.target.value, to })}
            className="h-8 w-[140px]"
          />
          <span className="text-muted-foreground">to</span>
          <Label htmlFor="analytics-to" className="sr-only">
            To
          </Label>
          <Input
            id="analytics-to"
            type="date"
            value={to}
            min={from}
            onChange={(e) => e.target.value && onChange({ from, to: e.target.value })}
            className="h-8 w-[140px]"
          />
        </div>
      )}
    </div>
  );
}
