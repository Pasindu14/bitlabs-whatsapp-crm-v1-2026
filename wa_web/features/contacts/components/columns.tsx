"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { User, MoreHorizontal, MessageSquare } from "lucide-react";
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
import type { Contact } from "@/features/contacts/types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const CONSENT_SOURCE_LABELS: Record<string, string> = {
  InboundMessage: "Messaged us first",
  ManualEntry: "Added manually",
  Import: "Imported",
  None: "—",
};

export interface ContactColumnActions {
  openEdit: (id: string) => void;
  openActivate: (id: string) => void;
  openDeactivate: (id: string) => void;
  openSendMessage: (id: string, name: string, phone: string) => void;
  /** One-click suppress / re-enable sending (flips IsOptedOut). */
  toggleOptOut: (contact: Contact) => void;
}

export function getContactColumns(actions: ContactColumnActions): ColumnDef<Contact>[] {
  const { openEdit, openActivate, openDeactivate, openSendMessage, toggleOptOut } = actions;

  return [
    {
      accessorKey: "name",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Contact" />,
      cell: ({ row }) => {
        const c = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <User className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{c.name}</div>
              <div className="truncate text-xs text-muted-foreground font-mono">{c.phone}</div>
            </div>
          </div>
        );
      },
      size: 280,
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
      id: "consent",
      header: "Consent",
      enableSorting: false,
      cell: ({ row }) => {
        const c = row.original;
        if (c.isOptedOut) {
          return (
            <Badge variant="destructive" title={c.optedOutAt ?? undefined}>
              Opted out
            </Badge>
          );
        }
        if (c.hasOptedIn) {
          return (
            <div className="flex flex-col">
              <Badge variant="default" className="w-fit">
                Opted in
              </Badge>
              <span className="mt-1 text-xs text-muted-foreground">
                {CONSENT_SOURCE_LABELS[c.consentSource] ?? c.consentSource}
              </span>
            </div>
          );
        }
        return (
          <Badge variant="secondary" title="No recorded opt-in consent">
            No consent
          </Badge>
        );
      },
      size: 150,
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
            <DropdownMenuContent align="end" className="w-44">
              <DropdownMenuItem onClick={() => openSendMessage(c.id, c.name, c.phone)}>
                <MessageSquare className="mr-2 h-4 w-4" />
                Send Message
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={() => openEdit(c.id)}>Edit</DropdownMenuItem>
              <DropdownMenuSeparator />
              {c.isOptedOut ? (
                <DropdownMenuItem onClick={() => toggleOptOut(c)}>
                  Re-enable sending
                </DropdownMenuItem>
              ) : (
                <DropdownMenuItem variant="destructive" onClick={() => toggleOptOut(c)}>
                  Suppress (opt out)
                </DropdownMenuItem>
              )}
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
