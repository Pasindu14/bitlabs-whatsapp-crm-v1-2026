"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { User, CheckCircle2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { DataTableColumnHeader } from "@/components/data-table/column-header";
import type { Message } from "@/features/messages/types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

export function getMessageColumns(): ColumnDef<Message>[] {
  return [
    {
      accessorKey: "contactName",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Contact" />,
      cell: ({ row }) => {
        const m = row.original;
        return (
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <User className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{m.contactName}</div>
              <div className="truncate text-xs text-muted-foreground font-mono">{m.contactPhone}</div>
            </div>
          </div>
        );
      },
      size: 220,
    },
    {
      accessorKey: "body",
      header: "Message",
      enableSorting: false,
      cell: ({ row }) => (
        <p className="max-w-sm truncate text-sm text-muted-foreground">
          {row.original.body}
        </p>
      ),
      size: 340,
    },
    {
      accessorKey: "displayPhoneNumber",
      header: "Sent From",
      enableSorting: false,
      cell: ({ row }) => (
        <span className="font-mono text-sm">{row.original.displayPhoneNumber}</span>
      ),
      size: 160,
    },
    {
      accessorKey: "status",
      header: "Status",
      cell: ({ row }) => {
        const sent = row.original.status === "Sent";
        return (
          <Badge variant={sent ? "default" : "destructive"} className="gap-1">
            {sent
              ? <CheckCircle2 className="h-3 w-3" />
              : <XCircle className="h-3 w-3" />}
            {row.original.status}
          </Badge>
        );
      },
      size: 110,
    },
    {
      accessorKey: "createdAt",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Sent At" />,
      cell: ({ row }) => (
        <span className="text-muted-foreground text-sm">
          {dateFmt.format(new Date(row.original.createdAt))}
        </span>
      ),
      size: 160,
    },
  ];
}
