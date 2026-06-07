"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { CreditCard, MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { DataTableColumnHeader } from "@/components/data-table/column-header";
import type { Subscription, SubscriptionStatus } from "@/features/subscriptions/types";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});
const numFmt = new Intl.NumberFormat("en-US");

const statusVariant: Record<SubscriptionStatus, "default" | "secondary" | "destructive" | "outline"> = {
  Active: "default",
  Trialing: "outline",
  Inactive: "secondary",
  Cancelled: "destructive",
};

export interface SubscriptionColumnActions {
  openChange: (companyId: string) => void;
  openCancel: (companyId: string) => void;
}

export function getSubscriptionColumns(
  actions: SubscriptionColumnActions
): ColumnDef<Subscription>[] {
  const { openChange, openCancel } = actions;

  return [
    {
      accessorKey: "company",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Company" />,
      cell: ({ row }) => {
        const s = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <CreditCard className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{s.companyName ?? "—"}</div>
              <div className="truncate text-xs text-muted-foreground">
                {s.planName ?? "No plan"}
              </div>
            </div>
          </div>
        );
      },
      size: 240,
    },
    {
      accessorKey: "plan",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Plan" />,
      cell: ({ row }) =>
        row.original.planName ?? <span className="text-muted-foreground">—</span>,
      size: 140,
    },
    {
      accessorKey: "status",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Status" />,
      cell: ({ row }) => {
        const status = row.original.status;
        if (!status) return <span className="text-muted-foreground">—</span>;
        return <Badge variant={statusVariant[status]}>{status}</Badge>;
      },
      size: 120,
    },
    {
      accessorKey: "usage",
      header: "Usage",
      enableSorting: false,
      cell: ({ row }) => {
        const s = row.original;
        if (!s.hasSubscription || s.monthlyMessageQuota === 0) {
          return <span className="text-muted-foreground">—</span>;
        }
        const pct = Math.min(
          100,
          Math.round((s.messagesUsedThisPeriod / s.monthlyMessageQuota) * 100)
        );
        return (
          <div className="min-w-[140px] space-y-1">
            <Progress value={pct} className="h-2" />
            <div className="text-xs text-muted-foreground">
              {numFmt.format(s.messagesUsedThisPeriod)} / {numFmt.format(s.monthlyMessageQuota)}
            </div>
          </div>
        );
      },
      size: 170,
    },
    {
      accessorKey: "currentPeriodEnd",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Renews" />,
      cell: ({ row }) => {
        const end = row.original.currentPeriodEnd;
        return (
          <span className="text-muted-foreground">
            {end ? dateFmt.format(new Date(end)) : "—"}
          </span>
        );
      },
      size: 130,
    },
    {
      id: "actions",
      header: "Actions",
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const s = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              <DropdownMenuItem onClick={() => openChange(s.companyId)}>
                Change plan
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem variant="destructive" onClick={() => openCancel(s.companyId)}>
                Cancel subscription
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
