"use client";

import { Download, ExternalLink, Receipt, AlertCircle, ShieldCheck, CreditCard } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
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
 * checkout) and SubscriptionPurchase rows — the latter cover BOTH a SuperAdmin manually
 * assigning/changing a plan AND a self-service PayHere checkout (distinguished by
 * `payHereOrderId`, since PayHere purchases apply via the same stack-or-fresh assignment
 * path, not a Stripe-style invoice). All three read as one "you were placed on a plan"
 * timeline from the company's point of view.
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
    <Card className="rounded-xl">
      <CardHeader>
        <CardTitle className="text-[15px]">Billing history</CardTitle>
        <CardDescription>Every plan you&apos;ve been put on or bought, newest first.</CardDescription>
      </CardHeader>

      <CardContent>
        {isLoading ? (
          <div className="space-y-2">
            {[1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-14 w-full rounded-lg" />
            ))}
          </div>
        ) : isError ? (
          <p className="flex items-center gap-2 text-sm text-destructive">
            <AlertCircle className="size-4" /> Billing history couldn&apos;t load. Refresh the page to try again.
          </p>
        ) : entries.length === 0 ? (
          <p className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
            <Receipt className="size-4" /> Nothing billed yet. Payments and plan changes will show here.
          </p>
        ) : (
          <ol className="relative">
            {/* The timeline spine. */}
            <span aria-hidden className="absolute top-2 bottom-2 left-[1.375rem] w-px bg-border" />
            {entries.map((entry) => (
              <li key={`${entry.kind}-${entry.data.id}`} className="relative flex items-start gap-4 py-3">
                <DateChip iso={entry.date} />
                {entry.kind === "invoice" ? <InvoiceRow invoice={entry.data} /> : <PurchaseRow purchase={entry.data} />}
              </li>
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  );
}

const dayFmt = new Intl.DateTimeFormat("en-US", { day: "numeric" });
const monFmt = new Intl.DateTimeFormat("en-US", { month: "short" });

function DateChip({ iso }: { iso: string }) {
  const d = new Date(iso);
  return (
    <span
      className="relative z-[1] flex size-11 shrink-0 flex-col items-center justify-center rounded-xl bg-card ring-1 ring-border"
      title={dateFmt.format(d)}
    >
      <span className="text-[15px] leading-none font-medium tabular-nums">{dayFmt.format(d)}</span>
      <span className="mt-0.5 text-[10.5px] leading-none text-muted-foreground">{monFmt.format(d)}</span>
    </span>
  );
}

function InvoiceRow({ invoice }: { invoice: Invoice }) {
  const paid = invoice.status === "paid";
  return (
    <div className="flex min-w-0 flex-1 items-center justify-between gap-3 pt-1">
      <div className="min-w-0">
        <p className="text-[14px] font-medium tabular-nums">{currFmt(invoice.amountPaid, invoice.currency)}</p>
        <p className="text-[12.5px] text-muted-foreground">
          Card payment, {invoice.paidAt ? dateFmt.format(new Date(invoice.paidAt)) : "not paid yet"}
        </p>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        <Badge variant={paid ? "default" : "secondary"} className="capitalize">
          {invoice.status}
        </Badge>
        {invoice.hostedInvoiceUrl && (
          <Button variant="ghost" size="icon" asChild className="size-7">
            <a href={invoice.hostedInvoiceUrl} target="_blank" rel="noopener noreferrer" aria-label="View invoice">
              <ExternalLink className="size-3.5" />
            </a>
          </Button>
        )}
        {invoice.invoicePdfUrl && (
          <Button variant="ghost" size="icon" asChild className="size-7">
            <a href={invoice.invoicePdfUrl} target="_blank" rel="noopener noreferrer" aria-label="Download invoice PDF">
              <Download className="size-3.5" />
            </a>
          </Button>
        )}
      </div>
    </div>
  );
}

function PurchaseRow({ purchase }: { purchase: SubscriptionPurchase }) {
  const viaPayHere = purchase.payHereOrderId !== null;
  const stacked = purchase.mode === "Stack";
  return (
    <div className="flex min-w-0 flex-1 items-center justify-between gap-3 pt-1">
      <div className="min-w-0">
        <p className="truncate text-[14px] font-medium">
          {purchase.planName}
          <span className="ml-2 font-normal text-muted-foreground tabular-nums">
            {currFmt(purchase.price, purchase.currency)}
          </span>
        </p>
        <p className="text-[12.5px] text-muted-foreground">
          {stacked
            ? "Messages added to your current plan"
            : viaPayHere
              ? "Bought with PayHere"
              : "Set up by your administrator"}
        </p>
      </div>
      <Badge
        variant="outline"
        className={
          viaPayHere
            ? "shrink-0 gap-1 border-emerald-600/30 text-emerald-700 dark:text-emerald-400"
            : "shrink-0 gap-1 text-muted-foreground"
        }
      >
        {viaPayHere ? <CreditCard className="size-3" /> : <ShieldCheck className="size-3" />}
        {viaPayHere ? "PayHere" : "Manual"}
      </Badge>
    </div>
  );
}
