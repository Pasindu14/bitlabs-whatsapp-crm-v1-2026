"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { TeamMember } from "@/features/team/types";
import { useTeamDataTable } from "@/features/team/hooks/use-team";
import {
  useTeamDialogStore,
  useEditTeamDialog,
  useResetTeamPasswordDialog,
  useActivateTeamDialog,
  useDeactivateTeamDialog,
} from "@/features/team/store/team-store";
import { getTeamColumns } from "./columns";
import { TeamDialogs } from "./team-dialogs";

export function TeamTable() {
  const openCreate = useTeamDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditTeamDialog();
  const { open: openResetPassword } = useResetTeamPasswordDialog();
  const { open: openActivate } = useActivateTeamDialog();
  const { open: openDeactivate } = useDeactivateTeamDialog();

  const getColumns = useCallback(
    () => getTeamColumns({ openEdit, openResetPassword, openActivate, openDeactivate }),
    [openEdit, openResetPassword, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<TeamMember, unknown>
        getColumns={getColumns}
        fetchDataFn={useTeamDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search users...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "team-table",
        }}
        exportConfig={{
          entityName: "team",
          columnMapping: {},
          columnWidths: [],
          headers: [],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={openCreate}>
            <Plus className="mr-1 h-4 w-4" />
            Add User
          </Button>
        )}
      />

      <TeamDialogs />
    </>
  );
}
