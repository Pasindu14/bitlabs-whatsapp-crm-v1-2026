"use client";

import { useState, type ReactNode } from "react";
import { ChevronDown, type LucideIcon } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { cn } from "@/lib/utils";

/** Soft colour per section so the page can be scanned by colour as well as by title. */
const TONES = {
  green: "bg-[#dcf1e3] text-[#0f6b3e]",
  blue: "bg-[#dcebfa] text-[#1b5e9e]",
  amber: "bg-[#fbe7d3] text-[#9a4f0f]",
  purple: "bg-[#ede3fa] text-[#5b3a9e]",
  pink: "bg-[#fadce4] text-[#9e2b4b]",
  teal: "bg-[#d9f2ef] text-[#0f6b66]",
  slate: "bg-muted text-muted-foreground",
} as const;
export type SectionTone = keyof typeof TONES;

/**
 * One collapsible section of the bot settings page. Click the header to open or close it.
 * <p>
 * The content stays mounted while closed (just hidden), so form fields inside keep their values and
 * stay registered with react-hook-form. <c>forceOpen</c> opens it regardless — used when a field
 * inside has a validation error, so the owner can see what needs fixing.
 */
export function SectionCard({
  title,
  description,
  icon: Icon,
  tone = "slate",
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
  /** Shown in a coloured tile at the start of the header. */
  icon?: LucideIcon;
  tone?: SectionTone;
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
      <Card className={cn("rounded-xl transition-shadow", isOpen && "shadow-[0_14px_34px_-22px_rgb(16_60_36/0.4)]")}>
        <CardHeader className="flex flex-row items-center justify-between gap-4 space-y-0">
          <CollapsibleTrigger className="group flex min-w-0 flex-1 cursor-pointer items-center gap-3 text-left focus-visible:outline-none">
            {Icon ? (
              <span className={cn("flex size-9 shrink-0 items-center justify-center rounded-lg", TONES[tone])}>
                <Icon className="size-[18px]" aria-hidden />
              </span>
            ) : null}
            <span className="block min-w-0 flex-1 space-y-0.5">
              <CardTitle className="text-[15px] font-medium">{title}</CardTitle>
              {description ? (
                <CardDescription className={cn("text-[12.5px] leading-relaxed", !isOpen && "line-clamp-1")}>
                  {description}
                </CardDescription>
              ) : null}
            </span>
            <ChevronDown
              className="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180"
              aria-hidden
            />
          </CollapsibleTrigger>
          {actions && (isOpen || actionsWhenClosed) ? actions : null}
        </CardHeader>

        <CollapsibleContent forceMount className="data-[state=closed]:hidden">
          <CardContent className={cn("space-y-4 border-t pt-4", contentClassName)}>{children}</CardContent>
        </CollapsibleContent>

        {after}
      </Card>
    </Collapsible>
  );
}
