"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { User as UserIcon, MoreHorizontal } from "lucide-react";
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
import type { User } from "@/features/users/types";
import type { UserRole } from "@/features/users/schema/user-schema";

const dateFmt = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const roleLabel: Record<UserRole, string> = {
  CompanyAdmin: "Company Admin",
  Agent: "Agent",
};

const roleVariant: Record<UserRole, "default" | "secondary"> = {
  CompanyAdmin: "default",
  Agent: "secondary",
};

export interface UserColumnActions {
  openEdit: (id: string) => void;
  openResetPassword: (id: string) => void;
  openActivate: (id: string) => void;
  openDeactivate: (id: string) => void;
}

export function getUserColumns(actions: UserColumnActions): ColumnDef<User>[] {
  const { openEdit, openResetPassword, openActivate, openDeactivate } = actions;

  return [
    {
      accessorKey: "fullName",
      header: ({ column }) => <DataTableColumnHeader column={column} title="User" />,
      cell: ({ row }) => {
        const u = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <UserIcon className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{u.fullName}</div>
              <div className="truncate text-xs text-muted-foreground">{u.email}</div>
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
      accessorKey: "role",
      header: "Role",
      enableSorting: false,
      cell: ({ row }) => (
        <Badge variant={roleVariant[row.original.role]}>{roleLabel[row.original.role]}</Badge>
      ),
      size: 140,
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
      accessorKey: "lastLoginAt",
      header: "Last Login",
      enableSorting: false,
      cell: ({ row }) =>
        row.original.lastLoginAt ? (
          <span className="text-muted-foreground">
            {dateFmt.format(new Date(row.original.lastLoginAt))}
          </span>
        ) : (
          <span className="text-muted-foreground">Never</span>
        ),
      size: 130,
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
        const u = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-40">
              <DropdownMenuItem onClick={() => openEdit(u.id)}>Edit</DropdownMenuItem>
              <DropdownMenuItem onClick={() => openResetPassword(u.id)}>
                Reset Password
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              {u.isActive ? (
                <DropdownMenuItem variant="destructive" onClick={() => openDeactivate(u.id)}>
                  Deactivate
                </DropdownMenuItem>
              ) : (
                <DropdownMenuItem onClick={() => openActivate(u.id)}>Activate</DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
