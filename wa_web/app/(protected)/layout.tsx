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
  
  const formatSegment = (segment: string) =>
    segment
      .split('-')
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
      .join(' ');

  return (
    <SessionGuard>
      <TooltipProvider>
        <SidebarProvider open={sidebarOpen} onOpenChange={handleSidebarChange}>
          <AppSidebar />
          <SidebarInset>
            <header className="flex h-12 shrink-0 items-center gap-2 transition-[width,height] ease-linear group-has-[[data-collapsible=icon]]/sidebar-wrapper:h-10">
              <div className="flex items-center gap-2 px-4">
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
            </header>
            <Separator />
            {children}
          </SidebarInset>
        </SidebarProvider>
      </TooltipProvider>
    </SessionGuard>
  );
}
