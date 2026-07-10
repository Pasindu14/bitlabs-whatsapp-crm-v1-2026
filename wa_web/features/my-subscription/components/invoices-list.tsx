"use client";

import { Download, ExternalLink, Receipt, AlertCircle, ShieldCheck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useInvoices, useSubscriptionHistory } from "@/features/my-subscription/hooks/use-my-subscription";
import type { Invoice, SubscriptionPurchase } from "@/features/my-subscription/types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const currFmt = (amount: number, currency: string) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);

type HistoryEntry =
  | { kind: "invoice"; date: string; data: Invoice }
  | { kind: "manual"; date: string; data: SubscriptionPurchase };

/**
 * CompanyAdmin-only billing history. Merges two sources: Stripe invoices (self-service
 * checkout) and SubscriptionPurchase rows (SuperAdmin manually assigning/changing a plan —
 * the only path in this phase, since Stripe is deferred). Both are "you were placed on a
 * plan" events from the company's point of view, so they read as one timeline.
 */
export function InvoicesList() {
  const { data: invoices, isLoading: invoicesLoading, isError: invoicesError } = useInvoices();
  const {
    data: history,
    isLoading: historyLoading,
    isError: historyError,
  } = useSubscriptionHistory();

  const isLoading = invoicesLoading || historyLoading;
  const isError = invoicesError || historyError;

  const entries: HistoryEntry[] = [
    ...(invoices ?? []).map((data) => ({
      kind: "invoice" as const,
      date: data.paidAt ?? data.createdAt,
      data,
    })),
    ...(history ?? []).map((data) => ({
      kind: "manual" as const,
      date: data.createdAt,
      data,
    })),
  ].sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime());

  return (
    <div className="flex flex-col gap-4 rounded-xl border bg-card p-6 shadow-sm">
      <div className="flex items-center gap-2">
        <Receipt className="h-4 w-4 text-muted-foreground" />
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Invoice History
        </p>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-12 w-full rounded-lg" />
          ))}
        </div>
      ) : isError ? (
        <div className="flex items-center gap-2 text-sm text-destructive">
          <AlertCircle className="h-4 w-4" />
          Could not load invoice history.
        </div>
      ) : entries.length === 0 ? (
        <p className="text-sm text-muted-foreground">No invoices yet.</p>
      ) : (
        <div className="divide-y">
          {entries.map((entry) =>
            entry.kind === "invoice" ? (
              <div
                key={`invoice-${entry.data.id}`}
                className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">
                    {currFmt(entry.data.amountPaid, entry.data.currency)}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {entry.data.paidAt ? dateFmt.format(new Date(entry.data.paidAt)) : "—"}
                  </p>
                </div>

                <Badge
                  variant={entry.data.status === "paid" ? "default" : "secondary"}
                  className="shrink-0 capitalize"
                >
                  {entry.data.status}
                </Badge>

                <div className="flex shrink-0 items-center gap-1">
                  {entry.data.hostedInvoiceUrl && (
                    <Button variant="ghost" size="icon" asChild className="h-7 w-7">
                      <a
                        href={entry.data.hostedInvoiceUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        title="View invoice"
                      >
                        <ExternalLink className="h-3.5 w-3.5" />
                      </a>
                    </Button>
                  )}
                  {entry.data.invoicePdfUrl && (
                    <Button variant="ghost" size="icon" asChild className="h-7 w-7">
                      <a
                        href={entry.data.invoicePdfUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        title="Download PDF"
                      >
                        <Download className="h-3.5 w-3.5" />
                      </a>
                    </Button>
                  )}
                </div>
              </div>
            ) : (
              <div
                key={`manual-${entry.data.id}`}
                className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">
                    {entry.data.planName}
                    <span className="ml-2 font-normal text-muted-foreground">
                      {currFmt(entry.data.price, entry.data.currency)}
                    </span>
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {dateFmt.format(new Date(entry.data.createdAt))} ·{" "}
                    {entry.data.mode === "Stack" ? "Added to existing plan" : "Assigned by admin"}
                  </p>
                </div>

                <Badge
                  variant="outline"
                  className="shrink-0 gap-1 text-muted-foreground"
                >
                  <ShieldCheck className="size-3" />
                  Manual
                </Badge>
              </div>
            )
          )}
        </div>
      )}
    </div>
  );
}
