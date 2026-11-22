import { TrendingDown, TrendingUp, type LucideIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardAction, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";

interface KpiCardProps {
  label: string;
  /** Pre-formatted value; null renders a dash (the fetch failed). */
  value: string | null;
  icon: LucideIcon;
  /** One short line under the number. */
  footnote: string;
  /** Percent change vs the previous period. Null hides the trend badge. */
  trend?: number | null;
}

/** A single headline number. Presentational, so it renders inside the server dashboard. */
export function KpiCard({ label, value, icon: Icon, footnote, trend = null }: KpiCardProps) {
  const up = (trend ?? 0) >= 0;

  return (
    <Card className="bg-gradient-to-t from-primary/[0.04] to-card">
      <CardHeader>
        <CardDescription className="flex items-center gap-1.5">
          <Icon className="size-3.5" />
          {label}
        </CardDescription>
        <CardTitle className="text-2xl font-semibold tabular-nums tracking-tight">{value ?? "—"}</CardTitle>
        {trend !== null && (
          <CardAction>
            <Badge
              variant="outline"
              className={cn(
                "gap-1 tabular-nums",
                up ? "text-primary" : "text-destructive",
              )}
            >
              {up ? <TrendingUp /> : <TrendingDown />}
              {up ? "+" : ""}
              {trend.toFixed(0)}%
            </Badge>
          </CardAction>
        )}
      </CardHeader>
      <CardFooter className="text-muted-foreground">{footnote}</CardFooter>
    </Card>
  );
}
