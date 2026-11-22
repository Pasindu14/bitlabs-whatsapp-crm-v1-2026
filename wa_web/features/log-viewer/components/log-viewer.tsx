"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowDownToLine, Eraser, Pause, Play, Search } from "lucide-react";
import { getLogsAction, type LogEntry } from "@/features/log-viewer/actions/log-actions";
import { Button } from "@/components/ui/button";
import { InputGroup, InputGroupAddon, InputGroupInput } from "@/components/ui/input-group";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { cn } from "@/lib/utils";

/** Chip colour and short label per Serilog level. */
const LEVEL: Record<string, { label: string; chip: string; row?: string }> = {
  Verbose: { label: "Trace", chip: "bg-white/5 text-[#7d8f85]" },
  Debug: { label: "Debug", chip: "bg-white/5 text-[#9fb0a6]" },
  Information: { label: "Info", chip: "bg-[#1fbf5c]/15 text-[#7fd6a0]" },
  Warning: { label: "Warn", chip: "bg-amber-400/15 text-amber-300", row: "border-l-amber-400/70 bg-amber-400/[0.04]" },
  Error: { label: "Error", chip: "bg-[#e5533d]/20 text-[#ff9f80]", row: "border-l-[#e5533d] bg-[#e5533d]/[0.06]" },
  Fatal: { label: "Fatal", chip: "bg-[#e5533d] text-white", row: "border-l-[#e5533d] bg-[#e5533d]/[0.1]" },
};

/**
 * Framework chatter that buries the lines a person is looking for: EF SQL, MVC pipeline steps and
 * result-writing notices. Hidden by default, counted, and one switch away.
 */
const ROUTINE = [
  /^Executed DbCommand/,
  /^Executing endpoint/,
  /^Executed endpoint/,
  /^Route matched/,
  // ASP.NET quotes the result type: Executing "OkObjectResult", writing value of type ...
  /^Executing "?\w*Result"?/,
  /^Executed action/,
  /^Executing action/,
];
const isRoutine = (e: LogEntry) => (e.level === "Information" || e.level === "Debug" || e.level === "Verbose") && ROUTINE.some((r) => r.test(e.message));

type LevelFilter = "all" | "warnings" | "errors";
const isWarning = (e: LogEntry) => e.level === "Warning";
const isError = (e: LogEntry) => e.level === "Error" || e.level === "Fatal";

function formatTs(ts: string) {
  const d = new Date(ts);
  return d.toLocaleTimeString("en-US", { hour12: false }) + "." + String(d.getMilliseconds()).padStart(3, "0");
}

export function LogViewer() {
  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [filter, setFilter] = useState("");
  const [level, setLevel] = useState<LevelFilter>("all");
  const [hideRoutine, setHideRoutine] = useState(true);
  const [autoScroll, setAutoScroll] = useState(false);
  const [paused, setPaused] = useState(false);
  const sinceRef = useRef<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const pausedRef = useRef(false);

  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  useEffect(() => {
    let active = true;

    async function poll() {
      if (!active) return;
      if (!pausedRef.current) {
        const result = await getLogsAction(sinceRef.current);
        if (result.success && result.data.length > 0) {
          sinceRef.current = result.data[result.data.length - 1].timestamp;
          setEntries((prev) => [...prev, ...result.data].slice(-100));
        }
      }
      if (active) setTimeout(poll, 2000);
    }

    getLogsAction(null).then((result) => {
      if (result.success && result.data.length > 0) {
        setEntries(result.data);
        sinceRef.current = result.data[result.data.length - 1].timestamp;
      }
    });

    const t = setTimeout(poll, 2000);
    return () => {
      active = false;
      clearTimeout(t);
    };
  }, []);

  useEffect(() => {
    if (autoScroll) bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [entries, autoScroll]);

  const counts = useMemo(
    () => ({
      warnings: entries.filter(isWarning).length,
      errors: entries.filter(isError).length,
      routine: entries.filter(isRoutine).length,
    }),
    [entries],
  );

  const visible = useMemo(() => {
    const q = filter.toLowerCase();
    return entries.filter((e) => {
      if (level === "warnings" && !isWarning(e)) return false;
      if (level === "errors" && !isError(e)) return false;
      if (hideRoutine && isRoutine(e)) return false;
      if (!q) return true;
      return (
        e.message.toLowerCase().includes(q) ||
        e.level.toLowerCase().includes(q) ||
        (e.exception ?? "").toLowerCase().includes(q)
      );
    });
  }, [entries, filter, level, hideRoutine]);

  return (
    <div className="flex h-full flex-col gap-3">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2">
        <InputGroup className="h-9 w-full rounded-lg bg-card sm:w-64">
          <InputGroupAddon className="pl-3">
            <Search className="size-4" />
          </InputGroupAddon>
          <InputGroupInput
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Search the logs"
            aria-label="Search the logs"
            className="h-full text-sm"
          />
        </InputGroup>

        <ToggleGroup
          type="single"
          value={level}
          onValueChange={(v) => v && setLevel(v as LevelFilter)}
          className="gap-0.5 rounded-lg bg-muted p-0.5"
          aria-label="Show log levels"
        >
          <LevelTab value="all" label="All" />
          <LevelTab value="warnings" label="Warnings" count={counts.warnings} dot="bg-amber-400" />
          <LevelTab value="errors" label="Errors" count={counts.errors} dot="bg-[#e5533d]" />
        </ToggleGroup>

        <div className="flex items-center gap-2 rounded-lg border bg-card px-3 py-1.5">
          <Switch id="hide-routine" checked={hideRoutine} onCheckedChange={setHideRoutine} />
          <Label htmlFor="hide-routine" className="text-[13px] font-normal">
            Hide routine lines
          </Label>
        </div>

        <div className="ml-auto flex items-center gap-1.5">
          <Button type="button" variant="outline" size="sm" onClick={() => setPaused((p) => !p)} className="rounded-full">
            {paused ? <Play data-icon="inline-start" /> : <Pause data-icon="inline-start" />}
            {paused ? "Resume" : "Pause"}
          </Button>
          <Button
            type="button"
            variant={autoScroll ? "default" : "outline"}
            size="sm"
            onClick={() => setAutoScroll((a) => !a)}
            aria-pressed={autoScroll}
            className="rounded-full"
          >
            <ArrowDownToLine data-icon="inline-start" />
            Follow
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => {
              setEntries([]);
              sinceRef.current = null;
            }}
            className="rounded-full"
          >
            <Eraser data-icon="inline-start" />
            Clear
          </Button>
        </div>
      </div>

      {/* Console */}
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl border border-[#1d2b24] bg-[#0f1a15] shadow-[0_14px_34px_-24px_rgb(0_0_0/0.6)]">
        <div className="flex items-center justify-between border-b border-white/5 px-4 py-2 text-[12px] text-[#9fb0a6]">
          <span className="flex items-center gap-2">
            <span
              className={cn(
                "size-2 rounded-full",
                paused ? "bg-amber-400" : "animate-pulse bg-[#1fbf5c] motion-reduce:animate-none",
              )}
            />
            {paused ? "Paused" : "Live"}
          </span>
          <span className="tabular-nums">
            {visible.length} shown
            {hideRoutine && counts.routine > 0 ? `, ${counts.routine} routine lines hidden` : ""}
          </span>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto py-1 font-mono text-[12px] leading-5">
          {visible.length === 0 && (
            <p className="px-4 py-6 text-[#7d8f85]">
              {entries.length === 0 ? "Waiting for the API to log something…" : "Nothing matches these filters."}
            </p>
          )}
          {visible.map((e, i) => {
            const meta = LEVEL[e.level] ?? { label: e.level, chip: "bg-white/5 text-[#9fb0a6]" };
            return (
              <div
                key={`${e.timestamp}-${i}`}
                className={cn("grid grid-cols-[6.5rem_3.25rem_minmax(0,1fr)] gap-3 border-l-2 border-l-transparent px-4 py-0.5", meta.row)}
              >
                <span className="text-[#5f7268] tabular-nums">{formatTs(e.timestamp)}</span>
                <span className={cn("h-fit rounded px-1.5 text-center text-[10.5px] font-semibold", meta.chip)}>{meta.label}</span>
                <span className="break-words whitespace-pre-wrap text-[#dbe7df]">
                  {e.message}
                  {e.exception && (
                    <span className="mt-1 block text-[11px] whitespace-pre-wrap text-[#ff9f80]/90">{e.exception}</span>
                  )}
                </span>
              </div>
            );
          })}
          <div ref={bottomRef} />
        </div>
      </div>
    </div>
  );
}

function LevelTab({ value, label, count, dot }: { value: LevelFilter; label: string; count?: number; dot?: string }) {
  return (
    <ToggleGroupItem
      value={value}
      className="h-8 gap-1.5 rounded-md border-0 px-3 text-[13px] font-normal text-muted-foreground data-[state=on]:bg-card data-[state=on]:font-medium data-[state=on]:text-foreground data-[state=on]:shadow-[0_1px_2px_rgb(0_0_0/0.08)]"
    >
      {dot && <span className={cn("size-1.5 rounded-full", dot)} />}
      {label}
      {count ? <span className="tabular-nums text-muted-foreground">{count}</span> : null}
    </ToggleGroupItem>
  );
}
