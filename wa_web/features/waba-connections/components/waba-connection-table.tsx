"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { WabaConnection } from "@/features/waba-connections/types";
import { useWabaConnectionDataTable } from "@/features/waba-connections/hooks/use-waba-connections";
import {
  useWabaConnectionDialogStore,
  useEditWabaConnectionDialog,
  useActivateWabaConnectionDialog,
  useDeactivateWabaConnectionDialog,
} from "@/features/waba-connections/store/waba-connection-store";
import { getWabaConnectionColumns } from "./columns";
import { WabaConnectionDialogs } from "./waba-connection-dialogs";

export function WabaConnectionTable() {
  const openCreate = useWabaConnectionDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditWabaConnectionDialog();
  const { open: openActivate } = useActivateWabaConnectionDialog();
  const { open: openDeactivate } = useDeactivateWabaConnectionDialog();

  const getColumns = useCallback(
    () => getWabaConnectionColumns({ openEdit, openActivate, openDeactivate }),
    [openEdit, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<WabaConnection, unknown>
        getColumns={getColumns}
        fetchDataFn={useWabaConnectionDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search connections...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "waba-connections-table",
        }}
        exportConfig={{
          entityName: "waba-connections",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add Connection
          </Button>
        )}
      />

      <WabaConnectionDialogs />
    </>
  );
}
