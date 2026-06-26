"use client";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export const numFmt = new Intl.NumberFormat("en-US");
export const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

export function money(amount: number, currency = "USD") {
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);
  } catch {
    return `${currency} ${numFmt.format(amount)}`;
  }
}

/** YYYY-MM-DD for an <input type="date"> value, n days before today. */
export function isoDaysAgo(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

export function isoToday(): string {
  return new Date().toISOString().slice(0, 10);
}

interface DateRangeFilterProps {
  from: string;
  to: string;
  onFrom: (v: string) => void;
  onTo: (v: string) => void;
  onPreset: (days: number) => void;
}

export function DateRangeFilter({ from, to, onFrom, onTo, onPreset }: DateRangeFilterProps) {
  const presets = [
    { label: "7d", days: 7 },
    { label: "30d", days: 30 },
    { label: "90d", days: 90 },
  ];
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1">
        <label className="text-xs font-medium text-muted-foreground">From</label>
        <Input
          type="date"
          value={from}
          max={to}
          onChange={(e) => onFrom(e.target.value)}
          className="w-[160px]"
        />
      </div>
      <div className="flex flex-col gap-1">
        <label className="text-xs font-medium text-muted-foreground">To</label>
        <Input
          type="date"
          value={to}
          min={from}
          onChange={(e) => onTo(e.target.value)}
          className="w-[160px]"
        />
      </div>
      <div className="flex gap-1.5">
        {presets.map((p) => (
          <Button key={p.days} variant="outline" size="sm" onClick={() => onPreset(p.days)}>
            {p.label}
          </Button>
        ))}
      </div>
    </div>
  );
}

/** Build a CSV string and trigger a browser download. */
export function exportCsv(filename: string, headers: string[], rows: (string | number)[][]) {
  const escape = (v: string | number) => {
    const s = String(v);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const csv = [headers, ...rows].map((r) => r.map(escape).join(",")).join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
