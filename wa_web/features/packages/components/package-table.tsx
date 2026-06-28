"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { MessagePackage } from "@/features/packages/types";
import { usePackageDataTable } from "@/features/packages/hooks/use-packages";
import {
  usePackageDialogStore,
  useEditPackageDialog,
  useActivatePackageDialog,
  useDeactivatePackageDialog,
} from "@/features/packages/store/package-store";
import { getPackageColumns } from "./columns";
import { PackageDialogs } from "./package-dialogs";

export function PackageTable() {
  const openCreate = usePackageDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditPackageDialog();
  const { open: openActivate } = useActivatePackageDialog();
  const { open: openDeactivate } = useDeactivatePackageDialog();

  const getColumns = useCallback(
    () => getPackageColumns({ openEdit, openActivate, openDeactivate }),
    [openEdit, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<MessagePackage, unknown>
        getColumns={getColumns}
        fetchDataFn={usePackageDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search packages...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "packages-table",
        }}
        exportConfig={{
          entityName: "packages",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add Package
          </Button>
        )}
      />

      <PackageDialogs />
    </>
  );
}
