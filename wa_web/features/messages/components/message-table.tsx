"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { Message } from "@/features/messages/types";
import { useMessageDataTable } from "@/features/messages/hooks/use-messages";
import { useSendMessageDialog } from "@/features/messages/store/message-store";
import { SendMessageDialog } from "./send-message-dialog";
import { getMessageColumns } from "./columns";

export function MessageTable() {
  const { openNew } = useSendMessageDialog();
  const getColumns = useCallback(() => getMessageColumns(), []);

  return (
    <>
      <DataTable<Message, unknown>
        getColumns={getColumns}
        fetchDataFn={useMessageDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search by contact or message…",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "messages-table",
        }}
        exportConfig={{
          entityName: "messages",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openNew}>
            <Plus className="mr-1 h-4 w-4" />
            New Message
          </Button>
        )}
      />

      <SendMessageDialog />
    </>
  );
}
