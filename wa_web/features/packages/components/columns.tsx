"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { Package, MoreHorizontal } from "lucide-react";
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
import type { MessagePackage } from "@/features/packages/types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const numFmt = new Intl.NumberFormat("en-US");

function formatPrice(price: number, currency: string): string {
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(price);
  } catch {
    return `${numFmt.format(price)} ${currency}`;
  }
}

export interface PackageColumnActions {
  openEdit: (id: string) => void;
  openActivate: (id: string) => void;
  openDeactivate: (id: string) => void;
}

export function getPackageColumns(actions: PackageColumnActions): ColumnDef<MessagePackage>[] {
  const { openEdit, openActivate, openDeactivate } = actions;

  return [
    {
      accessorKey: "name",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Package" />,
      cell: ({ row }) => {
        const p = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <Package className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{p.name}</div>
              <div className="truncate text-xs text-muted-foreground">
                {p.description || formatPrice(p.price, p.currency)}
              </div>
            </div>
          </div>
        );
      },
      size: 260,
    },
    {
      accessorKey: "messages",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Extra Messages" />,
      cell: ({ row }) => (
        <span>+{numFmt.format(row.original.extraMessages)} msgs</span>
      ),
      size: 160,
    },
    {
      accessorKey: "price",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Price" />,
      cell: ({ row }) => (
        <span>{formatPrice(row.original.price, row.original.currency)}</span>
      ),
      size: 120,
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
        const p = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-40">
              <DropdownMenuItem onClick={() => openEdit(p.id)}>Edit</DropdownMenuItem>
              <DropdownMenuSeparator />
              {p.isActive ? (
                <DropdownMenuItem variant="destructive" onClick={() => openDeactivate(p.id)}>
                  Deactivate
                </DropdownMenuItem>
              ) : (
                <DropdownMenuItem onClick={() => openActivate(p.id)}>Activate</DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
