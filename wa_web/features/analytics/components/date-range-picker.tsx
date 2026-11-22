"use client";

import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface Props {
  from: string;
  to: string;
  onChange: (range: { from: string; to: string }) => void;
}

export function AnalyticsDateRangePicker({ from, to, onChange }: Props) {
  return (
    <div className="flex items-center gap-2 text-sm">
      <Label htmlFor="analytics-from" className="font-normal text-muted-foreground">
        From
      </Label>
      <Input
        id="analytics-from"
        type="date"
        value={from}
        max={to}
        onChange={(e) => onChange({ from: e.target.value, to })}
        className="w-[150px]"
      />
      <Label htmlFor="analytics-to" className="font-normal text-muted-foreground">
        To
      </Label>
      <Input
        id="analytics-to"
        type="date"
        value={to}
        min={from}
        onChange={(e) => onChange({ from, to: e.target.value })}
        className="w-[150px]"
      />
    </div>
  );
}
