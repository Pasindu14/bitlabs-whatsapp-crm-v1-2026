"use client";

import { useState, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { cn } from "@/lib/utils";

/**
 * One collapsible section of the bot settings page. Click the title to open or close it.
 * <p>
 * The content stays mounted while closed (just hidden), so form fields inside keep their values and
 * stay registered with react-hook-form. <c>forceOpen</c> opens it regardless — used when a field
 * inside has a validation error, so the owner can see what needs fixing.
 */
export function SectionCard({
  title,
  description,
  actions,
  actionsWhenClosed = false,
  defaultOpen = false,
  forceOpen = false,
  contentClassName,
  children,
  after,
}: {
  title: ReactNode;
  description?: ReactNode;
  /** Buttons on the right of the header, e.g. "Add". Hidden while closed unless actionsWhenClosed. */
  actions?: ReactNode;
  actionsWhenClosed?: boolean;
  defaultOpen?: boolean;
  forceOpen?: boolean;
  contentClassName?: string;
  children: ReactNode;
  /** Rendered inside the card but outside the collapsing area, e.g. dialogs. */
  after?: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const isOpen = open || forceOpen;

  return (
    <Collapsible open={isOpen} onOpenChange={setOpen} asChild>
      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-4 space-y-0">
          <CollapsibleTrigger className="group flex flex-1 cursor-pointer items-start gap-2 text-left">
            <ChevronDown
              className="mt-0.5 size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=closed]:-rotate-90"
              aria-hidden
            />
            <span className="block space-y-1.5">
              <CardTitle>{title}</CardTitle>
              {description && isOpen ? <CardDescription>{description}</CardDescription> : null}
            </span>
          </CollapsibleTrigger>
          {actions && (isOpen || actionsWhenClosed) ? actions : null}
        </CardHeader>

        <CollapsibleContent forceMount className="data-[state=closed]:hidden">
          <CardContent className={cn("space-y-4", contentClassName)}>{children}</CardContent>
        </CollapsibleContent>

        {after}
      </Card>
    </Collapsible>
  );
}
