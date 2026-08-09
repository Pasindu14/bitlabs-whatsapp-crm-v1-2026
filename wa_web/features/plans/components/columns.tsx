"use client";

import type { ColumnDef } from "@tanstack/react-table";
import { Layers, MoreHorizontal } from "lucide-react";
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
import { permissionLabel } from "@/features/team/permissions";
import { useUsdToAedRate } from "@/features/fx/hooks/use-fx";
import { formatAedApprox } from "@/features/fx/format";
import type { Plan } from "@/features/plans/types";

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

/**
 * Own component rather than inline JSX so the FX hook has a real component to live in. Renders the
 * dirham equivalent only for USD plans — `formatAedApprox` returns null otherwise, which keeps the
 * existing AED-priced plans showing their true price instead of a 3.7×-inflated conversion.
 */
function PlanIdentityCell({ plan }: { plan: Plan }) {
  const { data: fxRate } = useUsdToAedRate();
  const aedApprox = formatAedApprox(plan.price, plan.currency, fxRate?.rate);

  return (
    <div className="flex items-center gap-3">
      <div className="flex h-9 w-9 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <Layers className="h-4 w-4" />
      </div>
      <div className="min-w-0">
        <div className="truncate font-medium">{plan.name}</div>
        <div className="truncate text-xs text-muted-foreground">
          {formatPrice(plan.price, plan.currency)} / mo
          {aedApprox && <span className="ml-1.5">({aedApprox})</span>}
        </div>
      </div>
    </div>
  );
}

export interface PlanColumnActions {
  openEdit: (id: string) => void;
  openActivate: (id: string) => void;
  openDeactivate: (id: string) => void;
}

export function getPlanColumns(actions: PlanColumnActions): ColumnDef<Plan>[] {
  const { openEdit, openActivate, openDeactivate } = actions;

  return [
    {
      accessorKey: "name",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Plan" />,
      cell: ({ row }) => <PlanIdentityCell plan={row.original} />,
      size: 220,
    },
    {
      accessorKey: "quota",
      header: ({ column }) => <DataTableColumnHeader column={column} title="Monthly Quota" />,
      cell: ({ row }) => (
        <span>{numFmt.format(row.original.monthlyMessageQuota)} msgs</span>
      ),
      size: 150,
    },
    {
      accessorKey: "featureFlags",
      header: "Features",
      enableSorting: false,
      cell: ({ row }) => {
        const flags = row.original.featureFlags ?? [];
        if (flags.length === 0) return <span className="text-muted-foreground">—</span>;
        const shown = flags.slice(0, 2);
        const extra = flags.length - shown.length;
        return (
          <div className="flex flex-wrap gap-1">
            {shown.map((f) => (
              <Badge key={f} variant="secondary" className="font-normal">
                {permissionLabel(f)}
              </Badge>
            ))}
            {extra > 0 && (
              <Badge variant="outline" className="font-normal">
                +{extra}
              </Badge>
            )}
          </div>
        );
      },
      size: 240,
    },
    {
      accessorKey: "isOnline",
      header: "Online",
      enableSorting: false,
      cell: ({ row }) =>
        row.original.isOnline ? (
          <Badge variant="default">Online</Badge>
        ) : (
          <span className="text-muted-foreground">—</span>
        ),
      size: 110,
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
