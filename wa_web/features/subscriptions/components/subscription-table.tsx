"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { Subscription } from "@/features/subscriptions/types";
import { useSubscriptionDataTable } from "@/features/subscriptions/hooks/use-subscriptions";
import {
  useSubscriptionDialogStore,
  useChangePlanDialog,
  useCancelSubscriptionDialog,
  useAddPackageDialog,
  useSubscriptionHistoryDialog,
} from "@/features/subscriptions/store/subscription-store";
import { getSubscriptionColumns } from "./columns";
import { SubscriptionDialogs } from "./subscription-dialogs";

export function SubscriptionTable() {
  const openAssign = useSubscriptionDialogStore((s) => s.openAssign);
  const { open: openChange } = useChangePlanDialog();
  const { open: openCancel } = useCancelSubscriptionDialog();
  const { open: openAddPackage } = useAddPackageDialog();
  const { open: openHistory } = useSubscriptionHistoryDialog();

  const getColumns = useCallback(
    () => getSubscriptionColumns({ openChange, openCancel, openAddPackage, openHistory }),
    [openChange, openCancel, openAddPackage, openHistory]
  );

  return (
    <>
      <DataTable<Subscription, unknown>
        getColumns={getColumns}
        fetchDataFn={useSubscriptionDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search by company or plan...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "subscriptions-table",
        }}
        exportConfig={{
          entityName: "subscriptions",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openAssign}>
            <Plus className="mr-1 h-4 w-4" />
            Assign Subscription
          </Button>
        )}
      />

      <SubscriptionDialogs />
    </>
  );
}
