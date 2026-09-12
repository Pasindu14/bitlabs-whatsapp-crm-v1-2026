"use client";
import Link from "next/link";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";

/**
 * The brand mark: WhatsApp's double "read" tick. Drawn inline rather than pulled from an icon
 * set so it stays identical to app/icon.svg — the two are the same logo in different places.
 * The 17×12 viewBox is wider than it is tall, so the box sizes it by width and lets the height
 * follow; forcing it square (size-4) would squash the ticks.
 */
function DoubleTick({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 17 12"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="M1 6.5 4.2 10 10 2.5M8 9l1 1 6.2-8" />
    </svg>
  );
}

export function CompanyLogo() {
  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <SidebarMenuButton size="lg" asChild>
          <Link href="/">
            <div className="flex aspect-square size-8 items-center justify-center rounded-lg bg-sidebar-primary text-sidebar-primary-foreground">
              <DoubleTick className="w-[18px]" />
            </div>
            <div className="grid flex-1 text-left text-sm leading-tight">
              <span className="truncate font-medium">Growchat</span>
            </div>
          </Link>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  );
}
