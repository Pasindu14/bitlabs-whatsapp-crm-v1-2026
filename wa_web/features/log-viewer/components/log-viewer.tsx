"use client";

import { useEffect, useRef, useState } from "react";
import { getLogsAction, type LogEntry } from "@/features/log-viewer/actions/log-actions";

const LEVEL_COLOR: Record<string, string> = {
  Verbose: "text-zinc-500",
  Debug: "text-zinc-400",
  Information: "text-emerald-400",
  Warning: "text-yellow-400",
  Error: "text-red-400",
  Fatal: "text-red-500 font-bold",
};

const LEVEL_BADGE: Record<string, string> = {
  Verbose: "VRB",
  Debug: "DBG",
  Information: "INF",
  Warning: "WRN",
  Error: "ERR",
  Fatal: "FTL",
};

function formatTs(ts: string) {
  const d = new Date(ts);
  return d.toLocaleTimeString("en-US", { hour12: false }) + "." + String(d.getMilliseconds()).padStart(3, "0");
}

export function LogViewer() {
  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [filter, setFilter] = useState("");
  const [autoScroll, setAutoScroll] = useState(false);
  const [paused, setPaused] = useState(false);
  const sinceRef = useRef<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const pausedRef = useRef(false);

  pausedRef.current = paused;

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

  const visible = filter
    ? entries.filter(
        (e) =>
          e.message.toLowerCase().includes(filter.toLowerCase()) ||
          e.level.toLowerCase().includes(filter.toLowerCase()) ||
          (e.exception ?? "").toLowerCase().includes(filter.toLowerCase())
      )
    : entries;

  return (
    <div className="flex flex-col gap-3 h-full">
      {/* Toolbar */}
      <div className="flex items-center gap-3 flex-wrap">
        <input
          type="text"
          placeholder="Filter logs…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="h-8 rounded-md border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring w-64"
        />
        <button
          onClick={() => setPaused((p) => !p)}
          className={`h-8 rounded-md px-3 text-xs font-medium border transition-colors ${
            paused
              ? "bg-yellow-500/10 border-yellow-500/30 text-yellow-400"
              : "bg-emerald-500/10 border-emerald-500/30 text-emerald-400"
          }`}
        >
          {paused ? "▶ Resume" : "⏸ Pause"}
        </button>
        <button
          onClick={() => setAutoScroll((a) => !a)}
          className={`h-8 rounded-md px-3 text-xs font-medium border transition-colors ${
            autoScroll
              ? "bg-primary/10 border-primary/30 text-primary"
              : "bg-muted border-border text-muted-foreground"
          }`}
        >
          ↓ Auto-scroll {autoScroll ? "on" : "off"}
        </button>
        <button
          onClick={() => { setEntries([]); sinceRef.current = null; }}
          className="h-8 rounded-md px-3 text-xs font-medium border border-border text-muted-foreground hover:text-foreground transition-colors"
        >
          Clear
        </button>
        <span className="text-xs text-muted-foreground ml-auto">
          {visible.length} {visible.length === 1 ? "entry" : "entries"}
          {paused && " · paused"}
        </span>
      </div>

      {/* Terminal */}
      <div className="flex-1 min-h-0 rounded-lg border border-border bg-zinc-950 overflow-y-auto font-mono text-xs p-4 space-y-0.5">
        {visible.length === 0 && (
          <div className="text-zinc-600 italic">Waiting for log entries…</div>
        )}
        {visible.map((e, i) => (
          <div key={i} className="leading-5 group">
            <span className="text-zinc-600">{formatTs(e.timestamp)} </span>
            <span className={`${LEVEL_COLOR[e.level] ?? "text-zinc-300"} mr-1`}>
              [{LEVEL_BADGE[e.level] ?? e.level.slice(0, 3).toUpperCase()}]
            </span>
            <span className="text-zinc-200">{e.message}</span>
            {e.exception && (
              <div className="mt-0.5 ml-12 text-red-400 whitespace-pre-wrap text-[10px] opacity-80">
                {e.exception}
              </div>
            )}
          </div>
        ))}
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
