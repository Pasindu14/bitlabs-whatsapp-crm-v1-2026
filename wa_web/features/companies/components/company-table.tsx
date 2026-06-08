"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { Company } from "@/features/companies/types";
import { useCompanyDataTable } from "@/features/companies/hooks/use-companies";
import {
  useCompanyDialogStore,
  useEditCompanyDialog,
  useActivateCompanyDialog,
  useDeactivateCompanyDialog,
} from "@/features/companies/store/company-store";
import { getCompanyColumns } from "./columns";
import { CompanyDialogs } from "./company-dialogs";

export function CompanyTable() {
  const openCreate = useCompanyDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditCompanyDialog();
  const { open: openActivate } = useActivateCompanyDialog();
  const { open: openDeactivate } = useDeactivateCompanyDialog();

  const getColumns = useCallback(
    () => getCompanyColumns({ openEdit, openActivate, openDeactivate }),
    [openEdit, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<Company, unknown>
        getColumns={getColumns}
        fetchDataFn={useCompanyDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search companies...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "companies-table",
        }}
        exportConfig={{
          entityName: "companies",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add Company
          </Button>
        )}
      />

      <CompanyDialogs />
    </>
  );
}
