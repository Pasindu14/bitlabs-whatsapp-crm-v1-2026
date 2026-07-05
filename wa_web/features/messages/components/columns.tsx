"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { User, CheckCircle2, XCircle, Clock } from "lucide-react";
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
        const status = row.original.status;
        // Failed is the only error state. "Accepted" is pending (queued at Meta, awaiting the 'sent'
        // webhook) — shown neutral with a clock. Sent/Delivered/Read all read as success.
        const failed = status === "Failed";
        const pending = status === "Accepted";
        const Icon = failed ? XCircle : pending ? Clock : CheckCircle2;
        return (
          <Badge variant={failed ? "destructive" : pending ? "secondary" : "default"} className="gap-1">
            <Icon className="h-3 w-3" />
            {status}
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
