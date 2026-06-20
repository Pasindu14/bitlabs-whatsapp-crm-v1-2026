"use client";

interface Props {
  from: string;
  to: string;
  onChange: (range: { from: string; to: string }) => void;
}

export function AnalyticsDateRangePicker({ from, to, onChange }: Props) {
  return (
    <div className="flex items-center gap-2 text-sm">
      <label className="text-muted-foreground">From</label>
      <input
        type="date"
        value={from}
        max={to}
        onChange={(e) => onChange({ from: e.target.value, to })}
        className="rounded-md border bg-background px-3 py-1.5 text-sm shadow-sm focus:outline-none focus:ring-1 focus:ring-primary"
      />
      <label className="text-muted-foreground">To</label>
      <input
        type="date"
        value={to}
        min={from}
        onChange={(e) => onChange({ from, to: e.target.value })}
        className="rounded-md border bg-background px-3 py-1.5 text-sm shadow-sm focus:outline-none focus:ring-1 focus:ring-primary"
      />
    </div>
  );
}
