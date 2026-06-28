"use client";

import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Send, Pause, Play, XCircle, Copy, Trash2, Users, BarChart3 } from "lucide-react";
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
import type { Campaign, CampaignStatus } from "@/features/campaigns/types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
});

const dateTimeFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

// Campaign schedules are entered + stored in UTC (the form labels the input "UTC"), so render the
// schedule in UTC too — otherwise the browser's local timezone shifts the displayed time (e.g.
// 3:22 PM UTC would show as 8:52 PM in UTC+5:30). The "UTC" suffix makes the zone explicit.
const scheduleUtcFmt = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  year: "numeric",
  hour: "numeric",
  minute: "2-digit",
  timeZone: "UTC",
  timeZoneName: "short",
});

const STATUS_VARIANT: Record<CampaignStatus, "default" | "secondary" | "destructive" | "outline"> =
  {
    Draft: "secondary",
    Scheduled: "outline",
    Running: "default",
    Paused: "secondary",
    Completed: "default",
    Failed: "destructive",
    Cancelled: "secondary",
  };

export interface CampaignColumnActions {
  openEdit: (id: string) => void;
  openRecipients: (id: string) => void;
  viewDetails: (id: string) => void;
  openDelete: (id: string) => void;
  openCancel: (id: string) => void;
  launch: (id: string) => void;
  pause: (id: string) => void;
  resume: (id: string) => void;
  duplicate: (id: string) => void;
}

export function getCampaignColumns(actions: CampaignColumnActions): ColumnDef<Campaign>[] {
  const { openEdit, openRecipients, viewDetails, openDelete, openCancel, launch, pause, resume, duplicate } =
    actions;

  return [
    {
      accessorKey: "name",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Campaign" />,
      cell: ({ row }) => {
        const c = row.original;
        const target =
          c.contactListNames.length > 0
            ? c.contactListNames.slice(0, 2).join(", ") +
              (c.contactListNames.length > 2 ? ` +${c.contactListNames.length - 2}` : "")
            : c.contactIds.length > 0
              ? `${c.contactIds.length} individual contact${c.contactIds.length !== 1 ? "s" : ""}`
              : "No target";
        return (
          <div className="min-w-0">
            <Link href={`/campaigns/${c.id}`} className="truncate font-medium hover:underline">
              {c.name}
            </Link>
            <div className="truncate text-xs text-muted-foreground">{c.templateName}</div>
            <div className="truncate text-xs text-muted-foreground">{target}</div>
          </div>
        );
      },
      size: 280,
    },
    {
      accessorKey: "status",
      header: "Status",
      enableSorting: false,
      cell: ({ row }) => {
        const s = row.original.status as CampaignStatus;
        return <Badge variant={STATUS_VARIANT[s]}>{s}</Badge>;
      },
      size: 110,
    },
    {
      accessorKey: "scheduleType",
      header: "Schedule",
      enableSorting: false,
      cell: ({ row }) => {
        const c = row.original;
        if (c.scheduleType === "Immediate") return <span className="text-sm">Immediate</span>;
        if (c.scheduleType === "OneTime" && c.scheduledAt) {
          return (
            <span className="text-sm text-muted-foreground">
              {scheduleUtcFmt.format(new Date(c.scheduledAt))}
            </span>
          );
        }
        if (c.scheduleType === "Recurring")
          return <span className="text-xs font-mono text-muted-foreground">{c.recurrenceCron}</span>;
        return null;
      },
      size: 150,
    },
    {
      accessorKey: "totalRecipients",
      header: "Recipients",
      enableSorting: false,
      cell: ({ row }) => {
        const c = row.original;
        if (c.totalRecipients === 0) return <span className="text-muted-foreground">—</span>;
        return (
          <div className="text-sm">
            <div>{c.sentCount.toLocaleString()} sent</div>
            <div className="text-xs text-muted-foreground">of {c.totalRecipients.toLocaleString()}</div>
          </div>
        );
      },
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
      size: 120,
    },
    {
      id: "actions",
      header: "Actions",
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) => {
        const c = row.original;
        const isDraft = c.status === "Draft";
        const isRunning = c.status === "Running";
        const isPaused = c.status === "Paused";
        const isScheduled = c.status === "Scheduled";
        const isTerminal =
          c.status === "Completed" || c.status === "Cancelled" || c.status === "Failed";
        const canLaunch = isDraft || isScheduled;

        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
                <span className="sr-only">Open actions</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              {canLaunch && (
                <DropdownMenuItem onClick={() => launch(c.id)}>
                  <Send className="mr-2 h-4 w-4" />
                  Launch
                </DropdownMenuItem>
              )}
              {isRunning && (
                <DropdownMenuItem onClick={() => pause(c.id)}>
                  <Pause className="mr-2 h-4 w-4" />
                  Pause
                </DropdownMenuItem>
              )}
              {isPaused && (
                <DropdownMenuItem onClick={() => resume(c.id)}>
                  <Play className="mr-2 h-4 w-4" />
                  Resume
                </DropdownMenuItem>
              )}
              <DropdownMenuItem onClick={() => viewDetails(c.id)}>
                <BarChart3 className="mr-2 h-4 w-4" />
                View details
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => openRecipients(c.id)}>
                <Users className="mr-2 h-4 w-4" />
                Quick stats
              </DropdownMenuItem>
              {isDraft && (
                <DropdownMenuItem onClick={() => openEdit(c.id)}>Edit</DropdownMenuItem>
              )}
              <DropdownMenuItem onClick={() => duplicate(c.id)}>
                <Copy className="mr-2 h-4 w-4" />
                Duplicate
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              {!isTerminal && (
                <DropdownMenuItem variant="destructive" onClick={() => openCancel(c.id)}>
                  <XCircle className="mr-2 h-4 w-4" />
                  Cancel
                </DropdownMenuItem>
              )}
              {(isDraft || c.status === "Cancelled") && (
                <DropdownMenuItem variant="destructive" onClick={() => openDelete(c.id)}>
                  <Trash2 className="mr-2 h-4 w-4" />
                  Delete
                </DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
      size: 80,
    },
  ];
}
