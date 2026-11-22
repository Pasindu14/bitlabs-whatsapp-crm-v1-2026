"use client";
import type { LucideIcon } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";
import { cn } from "@/lib/utils";

export type NavLink = { title: string; url: string; icon: LucideIcon };
export type NavSection = { label: string; items: NavLink[] };

/**
 * Flat, always-visible navigation: one link per screen, grouped under a quiet label. No collapsing —
 * every destination is one click away, and the icon keeps each one reachable when the sidebar is
 * folded to icons.
 */
export function NavMain({ sections }: { sections: NavSection[] }) {
  const pathname = usePathname();
  const isActive = (url: string) => pathname === url || pathname.startsWith(`${url}/`);

  return (
    <>
      {sections.map((section) => (
        <SidebarGroup key={section.label} className="py-1.5">
          <SidebarGroupLabel className="text-[11.5px] font-medium text-muted-foreground/80">
            {section.label}
          </SidebarGroupLabel>
          <SidebarMenu className="gap-0.5">
            {section.items.map((item) => {
              const active = isActive(item.url);
              return (
                <SidebarMenuItem key={item.url}>
                  <SidebarMenuButton
                    asChild
                    tooltip={item.title}
                    isActive={active}
                    className={cn(
                      "relative h-9 rounded-lg text-[13.5px] text-foreground/75 hover:text-foreground",
                      "data-[active=true]:bg-[#e3f1e8] data-[active=true]:font-medium data-[active=true]:text-[#0f6b3e]",
                      "dark:data-[active=true]:bg-primary/15 dark:data-[active=true]:text-[#7fd6a0]",
                    )}
                  >
                    <Link href={item.url} aria-current={active ? "page" : undefined}>
                      {/* Leaf bar on the active link, hidden when folded to icons. */}
                      {active && (
                        <span
                          aria-hidden
                          className="absolute top-1.5 bottom-1.5 left-0 w-[3px] rounded-full bg-[#0f6b3e] group-data-[collapsible=icon]:hidden"
                        />
                      )}
                      <item.icon className="size-[17px]" />
                      <span>{item.title}</span>
                    </Link>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              );
            })}
          </SidebarMenu>
        </SidebarGroup>
      ))}
    </>
  );
}
