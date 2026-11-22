"use client";

import { Button } from "@/components/ui/button";

// ** import types
import type { Row } from "@tanstack/react-table";

// ** import core packages
import { ChevronRight } from "lucide-react";

// ** import utils
import { cn } from "@/lib/utils";

interface ExpandIconProps<TData> {
  row: Row<TData>;
  className?: string;
  hideWhenSingle?: boolean;
}

export function ExpandIcon<TData>({
  row,
  className,
  hideWhenSingle = false
}: ExpandIconProps<TData>) {
  // Check if row can be expanded
  if (!row.getCanExpand()) {
    return <div className="w-4" />; // Spacer for alignment
  }

  // Hide icon if only 1 subrow and hideWhenSingle is true
  if (hideWhenSingle && row.subRows && row.subRows.length === 1) {
    return <div className="w-4" />;
  }

  return (
    <Button
      onClick={row.getToggleExpandedHandler()}
      variant="ghost"
      size="icon-xs"
      className={cn("rounded-sm", className)}
      aria-label={row.getIsExpanded() ? "Collapse row" : "Expand row"}
      aria-expanded={row.getIsExpanded()}
      type="button"
    >
      <ChevronRight
        className={cn(
          "h-4 w-4 transition-transform duration-200",
          row.getIsExpanded() && "rotate-90"
        )}
      />
    </Button>
  );
}
