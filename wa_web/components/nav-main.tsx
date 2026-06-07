"use client";
import { ChevronRight, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
} from "@/components/ui/sidebar";

export function NavMain({
  items,
}: {
  items: {
    title: string;
    url: string;
    icon?: LucideIcon;
    isActive?: boolean;
    items?: {
      title: string;
      url: string;
    }[];
  }[];
}) {
  const pathname = usePathname();

  function getActiveTitle() {
    return (
      items.find((item) =>
        item.items?.some((sub) => pathname.startsWith(sub.url))
      )?.title ?? null
    );
  }

  const [manualOpen, setManualOpen] = useState<{ pathname: string; opens: Set<string> }>({
    pathname,
    opens: new Set(),
  });

  const activeTitle = getActiveTitle();
  const effectiveOpens = manualOpen.pathname === pathname ? manualOpen.opens : new Set<string>();

  function isOpen(title: string) {
    return title === activeTitle || effectiveOpens.has(title);
  }

  function toggle(title: string) {
    if (title === activeTitle) return;
    setManualOpen((prev) => {
      const opens = new Set(prev.pathname === pathname ? prev.opens : []);
      if (opens.has(title)) {
        opens.delete(title);
      } else {
        opens.add(title);
      }
      return { pathname, opens };
    });
  }

  return (
    <SidebarGroup>
      <SidebarGroupLabel>Administration</SidebarGroupLabel>
      <SidebarMenu>
        {items.map((item) => (
          <Collapsible
            key={item.title}
            asChild
            open={isOpen(item.title)}
            onOpenChange={() => toggle(item.title)}
            className="group/collapsible"
          >
            <SidebarMenuItem>
              <CollapsibleTrigger asChild>
                <SidebarMenuButton tooltip={item.title}>
                  {item.icon && <item.icon />}
                  <span>{item.title}</span>
                  <ChevronRight className="ml-auto transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90" />
                </SidebarMenuButton>
              </CollapsibleTrigger>
              <CollapsibleContent>
                <SidebarMenuSub>
                  {item.items?.map((subItem) => (
                    <SidebarMenuSubItem key={subItem.title}>
                      <SidebarMenuSubButton asChild>
                        <Link
                          href={subItem.url}
                          className={
                            pathname === subItem.url
                              ? "bg-primary/10 text-primary font-medium"
                              : ""
                          }
                        >
                          <span>{subItem.title}</span>
                        </Link>
                      </SidebarMenuSubButton>
                    </SidebarMenuSubItem>
                  ))}
                </SidebarMenuSub>
              </CollapsibleContent>
            </SidebarMenuItem>
          </Collapsible>
        ))}
      </SidebarMenu>
    </SidebarGroup>
  );
}
