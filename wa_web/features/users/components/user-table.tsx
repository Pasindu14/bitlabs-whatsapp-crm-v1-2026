"use client";

import { useCallback } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import type { User } from "@/features/users/types";
import { useUserDataTable } from "@/features/users/hooks/use-users";
import {
  useUserDialogStore,
  useEditUserDialog,
  useResetPasswordDialog,
  useActivateUserDialog,
  useDeactivateUserDialog,
} from "@/features/users/store/user-store";
import { getUserColumns } from "./columns";
import { UserDialogs } from "./user-dialogs";

export function UserTable() {
  const openCreate = useUserDialogStore((s) => s.openCreate);
  const { open: openEdit } = useEditUserDialog();
  const { open: openResetPassword } = useResetPasswordDialog();
  const { open: openActivate } = useActivateUserDialog();
  const { open: openDeactivate } = useDeactivateUserDialog();

  const getColumns = useCallback(
    () => getUserColumns({ openEdit, openResetPassword, openActivate, openDeactivate }),
    [openEdit, openResetPassword, openActivate, openDeactivate]
  );

  return (
    <>
      <DataTable<User, unknown>
        getColumns={getColumns}
        fetchDataFn={useUserDataTable}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableExport: false,
          enableUrlState: false,
          searchPlaceholder: "Search users...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "users-table",
        }}
        exportConfig={{
          entityName: "users",
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

      <UserDialogs />
    </>
  );
}
