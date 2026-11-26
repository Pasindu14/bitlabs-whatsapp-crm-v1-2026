"use client";

import { useEffect } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";

/**
 * Deep link into a page action: visiting <c>?action=value</c> runs <paramref name="open"/> once (typically
 * opening a dialog), then drops the parameter so a refresh or "back" doesn't reopen it.
 */
export function useOpenFromQuery(value: string, open: () => void) {
  const params = useSearchParams();
  const pathname = usePathname();
  const router = useRouter();
  const matches = params.get("action") === value;

  useEffect(() => {
    if (!matches) return;
    open();
    router.replace(pathname, { scroll: false });
  }, [matches, open, pathname, router]);
}
