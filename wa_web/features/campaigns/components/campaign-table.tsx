"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { Campaign } from "@/features/campaigns/types";
import { useCampaignDataTable, useLaunchCampaign, usePauseCampaign, useResumeCampaign, useDuplicateCampaign } from "@/features/campaigns/hooks/use-campaigns";
import {
  useCampaignDialogStore,
  useEditCampaignDialog,
  useDeleteCampaignDialog,
  useCancelCampaignDialog,
  useRecipientsDialog,
} from "@/features/campaigns/store/campaign-store";
import { getCampaignColumns } from "./columns";
import { CampaignDialogs } from "./campaign-dialogs";

export function CampaignTable() {
  const openCreate = useCampaignDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditCampaignDialog();
  const { open: openDelete } = useDeleteCampaignDialog();
  const { open: openCancel } = useCancelCampaignDialog();
  const { open: openRecipients } = useRecipientsDialog();
  const { mutate: launch } = useLaunchCampaign();
  const { mutate: pause } = usePauseCampaign();
  const { mutate: resume } = useResumeCampaign();
  const { mutate: duplicate } = useDuplicateCampaign();

  const getColumns = useCallback(
    () =>
      getCampaignColumns({
        openEdit,
        openDelete,
        openCancel,
        openRecipients,
        launch,
        pause,
        resume,
        duplicate,
      }),
    [openEdit, openDelete, openCancel, openRecipients, launch, pause, resume, duplicate]
  );

  return (
    <>
      <DataTable<Campaign, unknown>
        getColumns={getColumns}
        fetchDataFn={useCampaignDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search campaigns...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "campaigns-table",
        }}
        exportConfig={{
          entityName: "campaigns",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            New Campaign
          </Button>
        )}
      />
      <CampaignDialogs />
    </>
  );
}
