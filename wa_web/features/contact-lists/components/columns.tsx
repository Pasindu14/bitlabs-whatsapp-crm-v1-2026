"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { Users, MoreHorizontal } from "lucide-react";
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
import type { ContactList } from "@/features/contact-lists/types";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});

export interface ContactListColumnActions {
  openEdit: (id: string) => void;
  openManage: (id: string) => void;
  openImport: (id: string) => void;
  openActivate: (id: string) => void;
  openDeactivate: (id: string) => void;
}

export function getContactListColumns(actions: ContactListColumnActions): ColumnDef<ContactList>[] {
  const { openEdit, openManage, openImport, openActivate, openDeactivate } = actions;

  return [
    {
      accessorKey: "name",
      header: ({ column }) => <DataTableColumnHeader column={column} title="List" />,
      cell: ({ row }) => {
        const l = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <Users className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{l.name}</div>
              {l.description && (
                <div className="truncate text-xs text-muted-foreground">{l.description}</div>
              )}
            </div>
          </div>
        );
      },
      size: 300,
    },
    {
      accessorKey: "contactCount",
      header: "Contacts",
      enableSorting: false,
      cell: ({ row }) => (
        <Badge variant="secondary">
          {row.original.contactCount} {row.original.contactCount === 1 ? "contact" : "contacts"}
        </Badge>
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
        const l = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              <DropdownMenuItem onClick={() => openManage(l.id)}>Manage contacts</DropdownMenuItem>
              <DropdownMenuItem onClick={() => openImport(l.id)}>Import contacts</DropdownMenuItem>
              <DropdownMenuItem onClick={() => openEdit(l.id)}>Edit</DropdownMenuItem>
              <DropdownMenuSeparator />
              {l.isActive ? (
                <DropdownMenuItem variant="destructive" onClick={() => openDeactivate(l.id)}>
                  Deactivate
                </DropdownMenuItem>
              ) : (
                <DropdownMenuItem onClick={() => openActivate(l.id)}>Activate</DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
