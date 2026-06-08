"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { FileText, MoreHorizontal, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { DataTableColumnHeader } from "@/components/data-table/column-header";
import { TemplateStatusBadge } from "./template-status-badge";
import type { TemplateRow } from "@/features/templates/types";

const dateFmt = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric" });

export interface TemplateColumnActions {
  onOpen: (id: string) => void;
  openSubmit: (id: string, name: string) => void;
  openDelete: (id: string, name: string) => void;
  onRefresh: (id: string) => void;
}

export function getTemplateColumns(actions: TemplateColumnActions): ColumnDef<TemplateRow>[] {
  const { onOpen, openSubmit, openDelete, onRefresh } = actions;

  return [
    {
      accessorKey: "name",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Template" />,
      cell: ({ row }) => {
        const t = row.original;
        return (
          <button
            type="button"
            onClick={() => onOpen(t.id)}
            className="flex items-center gap-3 text-left"
          >
            <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
              <FileText className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <div className="truncate font-medium">{t.name}</div>
              <div className="truncate text-xs text-muted-foreground uppercase">{t.language}</div>
            </div>
          </button>
        );
      },
      size: 300,
    },
    {
      accessorKey: "status",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Status" />,
      cell: ({ row }) => {
        const t = row.original;
        return (
          <div className="flex flex-col gap-1">
            <TemplateStatusBadge status={t.status} />
            {t.status === "Rejected" && t.rejectionReason && (
              <span className="max-w-[220px] truncate text-xs text-muted-foreground" title={t.rejectionReason}>
                {t.rejectionReason}
              </span>
            )}
          </div>
        );
      },
      size: 220,
    },
    {
      accessorKey: "createdAt",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Created" />,
      cell: ({ row }) => (
        <span className="text-muted-foreground">{dateFmt.format(new Date(row.original.createdAt))}</span>
      ),
      size: 130,
    },
    {
      id: "actions",
      header: "Actions",
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const t = row.original;
        const isDraft = t.status === "Draft";
        const isSubmitted = !!t.metaTemplateId;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-48">
              <DropdownMenuItem onClick={() => onOpen(t.id)}>
                {isDraft ? "Edit" : "View"}
              </DropdownMenuItem>
              {isDraft && (
                <DropdownMenuItem onClick={() => openSubmit(t.id, t.name)}>
                  Submit for approval
                </DropdownMenuItem>
              )}
              {isSubmitted && (
                <DropdownMenuItem onClick={() => onRefresh(t.id)}>
                  <RefreshCw className="mr-2 h-4 w-4" />
                  Refresh status
                </DropdownMenuItem>
              )}
              <DropdownMenuSeparator />
              <DropdownMenuItem variant="destructive" onClick={() => openDelete(t.id, t.name)}>
                Delete
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
