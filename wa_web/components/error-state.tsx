"use client";

import { AlertCircle } from "lucide-react";

export function ErrorState() {
  return (
    <div className="flex min-h-[400px] flex-col items-center justify-center gap-3 text-center">
      <AlertCircle className="h-8 w-8 text-destructive" />
      <h2 className="text-lg font-semibold">Something went wrong</h2>
      <p className="text-sm text-muted-foreground">
        An unexpected error occurred. Refresh the page to try again.
      </p>
    </div>
  );
}
