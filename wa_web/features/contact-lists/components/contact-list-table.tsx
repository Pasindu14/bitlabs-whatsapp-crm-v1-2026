"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { ContactList } from "@/features/contact-lists/types";
import { useContactListDataTable } from "@/features/contact-lists/hooks/use-contact-lists";
import {
  useContactListDialogStore,
  useEditContactListDialog,
  useActivateContactListDialog,
  useDeactivateContactListDialog,
  useManageContactsDialog,
  useImportContactsDialog,
} from "@/features/contact-lists/store/contact-list-store";
import { getContactListColumns } from "./columns";
import { ContactListDialogs } from "./contact-list-dialogs";

export function ContactListTable() {
  const openCreate = useContactListDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditContactListDialog();
  const { open: openManage } = useManageContactsDialog();
  const { open: openImport } = useImportContactsDialog();
  const { open: openActivate } = useActivateContactListDialog();
  const { open: openDeactivate } = useDeactivateContactListDialog();

  const getColumns = useCallback(
    () => getContactListColumns({ openEdit, openManage, openImport, openActivate, openDeactivate }),
    [openEdit, openManage, openImport, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<ContactList, unknown>
        getColumns={getColumns}
        fetchDataFn={useContactListDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search lists...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "contact-lists-table",
        }}
        exportConfig={{
          entityName: "contact-lists",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add List
          </Button>
        )}
      />

      <ContactListDialogs />
    </>
  );
}
