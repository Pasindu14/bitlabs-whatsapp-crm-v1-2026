"use client";

import { History } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { useSubscriptionHistoryDialog } from "@/features/subscriptions/store/subscription-store";
import { useSubscriptionHistory } from "@/features/subscriptions/hooks/use-subscriptions";

const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});
const numFmt = new Intl.NumberFormat("en-US");

export function SubscriptionHistoryDialog() {
  const { isOpen, selectedCompanyId, close } = useSubscriptionHistoryDialog();
  const { data, isLoading, isError } = useSubscriptionHistory(isOpen ? selectedCompanyId : null);

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && close()}>
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Subscription history</DialogTitle>
          <DialogDescription>
            Every subscribe for this company, newest first. Stacked subscribes add to the balance and
            extend the expiry; fresh ones start a new period.
          </DialogDescription>
        </DialogHeader>

        {isLoading && (
          <div className="flex items-center justify-center py-10 text-muted-foreground">
            <Spinner className="mr-2" />
            Loading history…
          </div>
        )}

        {isError && (
          <p className="py-10 text-center text-sm text-destructive">
            Could not load subscription history.
          </p>
        )}

        {!isLoading && !isError && (data?.length ?? 0) === 0 && (
          <div className="flex flex-col items-center gap-2 py-10 text-muted-foreground">
            <History className="h-6 w-6" />
            <p className="text-sm">No subscribes recorded yet.</p>
          </div>
        )}

        {!isLoading && !isError && (data?.length ?? 0) > 0 && (
          <ul className="divide-y">
            {data!.map((h) => (
              <li key={h.id} className="flex items-start justify-between gap-3 py-3">
                <div className="min-w-0 space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="truncate font-medium">{h.planName}</span>
                    <Badge variant={h.mode === "Stack" ? "default" : "secondary"}>
                      {h.mode === "Stack" ? "Stacked" : "Fresh"}
                    </Badge>
                  </div>
                  <div className="text-xs text-muted-foreground">
                    +{numFmt.format(h.messagesAdded)} msgs · +{h.periodDays} days
                  </div>
                  <div className="text-xs text-muted-foreground">
                    Balance after: {numFmt.format(h.balanceAfter)} · Expires{" "}
                    {dateFmt.format(new Date(h.periodEndAfter))}
                  </div>
                </div>
                <div className="shrink-0 text-right text-xs text-muted-foreground">
                  {dateFmt.format(new Date(h.createdAt))}
                </div>
              </li>
            ))}
          </ul>
        )}
      </DialogContent>
    </Dialog>
  );
}
