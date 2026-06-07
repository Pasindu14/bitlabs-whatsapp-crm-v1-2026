"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { MessageCircle, MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { DataTableColumnHeader } from "@/components/data-table/column-header";
import type { WabaConnection } from "@/features/waba-connections/types";
import type { WabaConnectionStatus } from "@/features/waba-connections/schema/waba-connection-schema";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const statusVariant: Record<WabaConnectionStatus, "default" | "secondary" | "destructive"> = {
  Connected: "default",
  Disconnected: "secondary",
  Invalid: "destructive",
};

export interface WabaConnectionColumnActions {
  openEdit: (id: string) => void;
  openActivate: (id: string) => void;
  openDeactivate: (id: string) => void;
}

export function getWabaConnectionColumns(
  actions: WabaConnectionColumnActions
): ColumnDef<WabaConnection>[] {
  const { openEdit, openActivate, openDeactivate } = actions;

  return [
    {
      accessorKey: "displayPhoneNumber",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Connection" />,
      cell: ({ row }) => {
        const c = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <MessageCircle className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">
                {c.displayPhoneNumber || "—"}
              </div>
              <div className="truncate text-xs text-muted-foreground">{c.phoneNumberId}</div>
            </div>
          </div>
        );
      },
      size: 260,
    },
    {
      accessorKey: "company",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Company" />,
      cell: ({ row }) =>
        row.original.companyName ?? <span className="text-muted-foreground">—</span>,
      size: 200,
    },
    {
      accessorKey: "wabaId",
      header: "WABA ID",
      enableSorting: false,
      cell: ({ row }) => (
        <span className="font-mono text-xs">{row.original.wabaId}</span>
      ),
      size: 180,
    },
    {
      accessorKey: "status",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Status" />,
      cell: ({ row }) => (
        <Badge variant={statusVariant[row.original.status]}>{row.original.status}</Badge>
      ),
      size: 130,
    },
    {
      accessorKey: "isActive",
      header: "Active",
      enableSorting: false,
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "default" : "secondary"}>
          {row.original.isActive ? "Active" : "Inactive"}
        </Badge>
      ),
      size: 110,
    },
    {
      accessorKey: "createdAt",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Created" />,
      cell: ({ row }) => (
        <span className="text-muted-foreground">
          {dateFmt.format(new Date(row.original.createdAt))}
        </span>
      ),
      size: 130,
    },
    {
      id: "actions",
      header: "Actions",
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const c = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-40">
              <DropdownMenuItem onClick={() => openEdit(c.id)}>Edit</DropdownMenuItem>
              <DropdownMenuSeparator />
              {c.isActive ? (
                <DropdownMenuItem variant="destructive" onClick={() => openDeactivate(c.id)}>
                  Deactivate
                </DropdownMenuItem>
              ) : (
                <DropdownMenuItem onClick={() => openActivate(c.id)}>Activate</DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
