import Link from "next/link";
import { ArrowRight, Megaphone } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import type { CampaignPerformanceRow } from "@/features/analytics/types";
import { pct } from "@/features/dashboard/format";

const STATUS_VARIANT: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  Draft: "secondary",
  Scheduled: "outline",
  Running: "default",
  Paused: "secondary",
  Completed: "default",
  Failed: "destructive",
  Cancelled: "secondary",
};

/** Latest campaigns with delivery and read progress. */
export function RecentCampaigns({ rows }: { rows: CampaignPerformanceRow[] | null }) {
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle>Recent campaigns</CardTitle>
        <CardDescription>How your latest sends are landing.</CardDescription>
        <CardAction>
          <Button asChild variant="ghost" size="sm">
            <Link href="/campaigns">
              All campaigns <ArrowRight data-icon="inline-end" />
            </Link>
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-1">
        {rows === null ? (
          <Empty text="Campaigns could not be loaded." />
        ) : rows.length === 0 ? (
          <Empty text="No campaigns in the last 90 days." />
        ) : (
          rows.map((r) => {
            const delivered = pct(r.delivered, r.sent);
            const read = pct(r.read, r.sent);
            return (
              <Link
                key={r.id}
                href={`/campaigns/${r.id}`}
                className="-mx-2 flex flex-col gap-2 rounded-md px-2 py-2.5 transition-colors hover:bg-muted/60"
              >
                <div className="flex items-center justify-between gap-2">
                  <p className="truncate font-medium">{r.name}</p>
                  <Badge variant={STATUS_VARIANT[r.status] ?? "outline"} className="shrink-0">
                    {r.status}
                  </Badge>
                </div>
                <Progress value={read ?? 0} className="h-1.5" />
                <div className="flex justify-between text-[0.7rem] text-muted-foreground tabular-nums">
                  <span>{r.sent.toLocaleString()} sent</span>
                  <span>
                    {delivered ?? 0}% delivered · {read ?? 0}% read
                  </span>
                </div>
              </Link>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}

function Empty({ text }: { text: string }) {
  return (
    <div className="flex flex-col items-center gap-2 py-10 text-center text-muted-foreground">
      <Megaphone className="size-5" />
      {text}
    </div>
  );
}
