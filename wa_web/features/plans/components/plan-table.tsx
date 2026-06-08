"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { Plan } from "@/features/plans/types";
import { usePlanDataTable } from "@/features/plans/hooks/use-plans";
import {
  usePlanDialogStore,
  useEditPlanDialog,
  useActivatePlanDialog,
  useDeactivatePlanDialog,
} from "@/features/plans/store/plan-store";
import { getPlanColumns } from "./columns";
import { PlanDialogs } from "./plan-dialogs";

export function PlanTable() {
  const openCreate = usePlanDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditPlanDialog();
  const { open: openActivate } = useActivatePlanDialog();
  const { open: openDeactivate } = useDeactivatePlanDialog();

  const getColumns = useCallback(
    () => getPlanColumns({ openEdit, openActivate, openDeactivate }),
    [openEdit, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<Plan, unknown>
        getColumns={getColumns}
        fetchDataFn={usePlanDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search plans...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "plans-table",
        }}
        exportConfig={{
          entityName: "plans",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add Plan
          </Button>
        )}
      />

      <PlanDialogs />
    </>
  );
}
