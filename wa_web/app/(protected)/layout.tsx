"use client";

import * as React from "react";
import { useState } from "react";
import dynamic from "next/dynamic";

const AppSidebar = dynamic(
  () => import("@/components/app-sidebar").then((m) => ({ default: m.AppSidebar })),
  { ssr: false }
);
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Separator } from "@/components/ui/separator";
import {
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
} from "@/components/ui/sidebar";
import { TooltipProvider } from "@/components/ui/tooltip";
import { usePathname } from "next/navigation";
import { SessionGuard } from "@/components/session-guard";
import { NotificationBell } from "@/components/notification-bell";

export default function ProtectedLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const pathname = usePathname();

  const [sidebarOpen, setSidebarOpen] = useState(() => {
    try {
      const stored = localStorage.getItem("sidebar:open");
      return stored !== null ? stored === "true" : false;
    } catch {
      return false;
    }
  });

  const handleSidebarChange = (open: boolean) => {
    setSidebarOpen(open);
    try {
      localStorage.setItem("sidebar:open", String(open));
    } catch {
      // ignore
    }
  };

  const pathSegments = pathname.split("/").filter(Boolean);
  
  // Sentence case, matching the sidebar and page titles: "my-subscription" → "My subscription".
  // A few segments read better under their sidebar name.
  const SEGMENT_LABELS: Record<string, string> = {
    bot: "Chatbot",
    analytics: "Reports",
    superadmin: "Platform",
    "waba-connections": "WhatsApp numbers",
    "server-logs": "Logs",
  };
  const formatSegment = (segment: string) => {
    if (SEGMENT_LABELS[segment]) return SEGMENT_LABELS[segment];
    // Record ids (campaigns/<uuid>, templates/<uuid>) mean nothing to a person.
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(segment)) return "Details";
    const words = segment.split("-").join(" ");
    return words.charAt(0).toUpperCase() + words.slice(1);
  };

  return (
    <SessionGuard>
      <TooltipProvider>
        <SidebarProvider open={sidebarOpen} onOpenChange={handleSidebarChange}>
          <AppSidebar />
          {/* bg-* here wins over the inset's own white so the canvas colour shows; the pattern comes from .app-canvas. */}
          <SidebarInset className="app-canvas bg-[#f4f8f5] dark:bg-background">
            <header className="flex h-12 shrink-0 items-center gap-2 bg-background transition-[width,height] ease-linear group-has-[[data-collapsible=icon]]/sidebar-wrapper:h-10">
              <div className="flex flex-1 items-center gap-2 px-4">
                <SidebarTrigger className="-ml-1" />
                <Separator
                  orientation="vertical"
                  className="mr-2 data-[orientation=vertical]:h-10"
                />
                <Breadcrumb>
                  <BreadcrumbList>
                    {pathSegments.map((segment, index) => {
                      const href =
                        "/" + pathSegments.slice(0, index + 1).join("/");
                      const isLast = index === pathSegments.length - 1;

                      return (
                        <React.Fragment key={segment}>
                          {index > 0 && (
                            <BreadcrumbSeparator className="hidden md:block" />
                          )}
                          <BreadcrumbItem className="hidden md:block">
                            {isLast ? (
                              <BreadcrumbPage>
                                {formatSegment(segment)}
                              </BreadcrumbPage>
                            ) : (
                              <BreadcrumbLink href={href}>
                                {formatSegment(segment)}
                              </BreadcrumbLink>
                            )}
                          </BreadcrumbItem>
                        </React.Fragment>
                      );
                    })}
                  </BreadcrumbList>
                </Breadcrumb>
              </div>
              <div className="flex items-center gap-1 px-4">
                <NotificationBell />
              </div>
            </header>
            <Separator />
            {children}
          </SidebarInset>
        </SidebarProvider>
      </TooltipProvider>
    </SessionGuard>
  );
}
