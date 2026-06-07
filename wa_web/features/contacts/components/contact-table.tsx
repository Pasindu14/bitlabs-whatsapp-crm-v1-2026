"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { Contact } from "@/features/contacts/types";
import { useContactDataTable } from "@/features/contacts/hooks/use-contacts";
import {
  useContactDialogStore,
  useEditContactDialog,
  useActivateContactDialog,
  useDeactivateContactDialog,
} from "@/features/contacts/store/contact-store";
import { useSendMessageDialog } from "@/features/messages/store/message-store";
import { getContactColumns } from "./columns";
import { ContactDialogs } from "./contact-dialogs";
import { SendMessageDialog } from "@/features/messages/components/send-message-dialog";

export function ContactTable() {
  const openCreate = useContactDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditContactDialog();
  const { open: openActivate } = useActivateContactDialog();
  const { open: openDeactivate } = useDeactivateContactDialog();
  const { open: openSendMessage } = useSendMessageDialog();

  const getColumns = useCallback(
    () => getContactColumns({ openEdit, openActivate, openDeactivate, openSendMessage }),
    [openEdit, openActivate, openDeactivate, openSendMessage]
  );

  return (
    <>
      <DataTable<Contact, unknown>
        getColumns={getColumns}
        fetchDataFn={useContactDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search contacts...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "contacts-table",
        }}
        exportConfig={{
          entityName: "contacts",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add Contact
          </Button>
        )}
      />

      <ContactDialogs />
      <SendMessageDialog />
    </>
  );
}
