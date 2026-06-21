"use client";

import { Download, ExternalLink, Receipt, AlertCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useInvoices } from "@/features/my-subscription/hooks/use-my-subscription";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const currFmt = (amount: number, currency: string) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency }).format(amount);

/**
 * CompanyAdmin-only invoice history.
 * Shows a list of Stripe invoices with links to the hosted invoice and PDF.
 */
export function InvoicesList() {
  const { data: invoices, isLoading, isError } = useInvoices();

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
          Could not load invoices.
        </div>
      ) : !invoices?.length ? (
        <p className="text-sm text-muted-foreground">No invoices yet.</p>
      ) : (
        <div className="divide-y">
          {invoices.map((inv) => (
            <div
              key={inv.id}
              className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
            >
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">
                  {currFmt(inv.amountPaid, inv.currency)}
                </p>
                <p className="text-xs text-muted-foreground">
                  {inv.paidAt ? dateFmt.format(new Date(inv.paidAt)) : "—"}
                </p>
              </div>

              <Badge
                variant={inv.status === "paid" ? "default" : "secondary"}
                className="shrink-0 capitalize"
              >
                {inv.status}
              </Badge>

              <div className="flex shrink-0 items-center gap-1">
                {inv.hostedInvoiceUrl && (
                  <Button variant="ghost" size="icon" asChild className="h-7 w-7">
                    <a
                      href={inv.hostedInvoiceUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      title="View invoice"
                    >
                      <ExternalLink className="h-3.5 w-3.5" />
                    </a>
                  </Button>
                )}
                {inv.invoicePdfUrl && (
                  <Button variant="ghost" size="icon" asChild className="h-7 w-7">
                    <a
                      href={inv.invoicePdfUrl}
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
          ))}
        </div>
      )}
    </div>
  );
}
