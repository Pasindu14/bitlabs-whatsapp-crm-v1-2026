"use client";

import { useCallback } from "react";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { TemplateRow } from "@/features/templates/types";
import { useTemplateDataTable, useRefreshTemplateStatus } from "@/features/templates/hooks/use-templates";
import { useSubmitTemplateDialog, useDeleteTemplateDialog } from "@/features/templates/store/template-store";
import { getTemplateColumns } from "./columns";
import { TemplateDialogs } from "./template-dialogs";

export function TemplateTable() {
  const router = useRouter();
  const { open: openSubmit } = useSubmitTemplateDialog();
  const { open: openDelete } = useDeleteTemplateDialog();
  const refresh = useRefreshTemplateStatus();

  const getColumns = useCallback(
    () =>
      getTemplateColumns({
        onOpen: (id) => router.push(`/templates/${id}`),
        openSubmit,
        openDelete,
        onRefresh: (id) => refresh.mutate(id),
      }),
    [router, openSubmit, openDelete, refresh]
  );

  return (
    <>
      <DataTable<TemplateRow, unknown>
        getColumns={getColumns}
        fetchDataFn={useTemplateDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search templates...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "templates-table",
        }}
        exportConfig={{
          entityName: "templates",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={() => router.push("/templates/new")}>
            <Plus className="mr-1 h-4 w-4" />
            New Template
          </Button>
        )}
      />

      <TemplateDialogs />
    </>
  );
}
