"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable } from "@/components/data-table/data-table";
import { getCompaniesAction } from "@/features/companies/actions/company-actions";
import type { Company } from "@/features/companies/types";
import { useCompanyStore } from "@/features/companies/store/company-store";
import { getCompanyColumns } from "./columns";
import { AddCompanyDialog } from "./add-company-dialog";
import { DeleteCompanyDialog } from "./delete-company-dialog";

/**
 * Adapts the @data-table positional fetch contract to getCompaniesAction.
 * Returns the shape the table expects: { success, data, pagination }.
 */
async function fetchCompanies(
  page: number,
  pageSize: number,
  search: string,
  _dateRange: { from_date: string; to_date: string },
  sortBy: string,
  sortOrder: string
) {
  const res = await getCompaniesAction({
    page,
    pageSize,
    search: search || undefined,
    sortBy: sortBy || undefined,
    sortOrder: (sortOrder as "asc" | "desc") || undefined,
  });

  if (!res.success) {
    return {
      success: false,
      data: [] as Company[],
      pagination: { page, limit: pageSize, total_pages: 0, total_items: 0 },
    };
  }

  const { items, pagination } = res.data;
  return {
    success: true,
    data: items,
    pagination: {
      page: pagination.page,
      limit: pagination.pageSize,
      total_pages: pagination.totalPages,
      total_items: pagination.total,
    },
  };
}

export function CompanyTable() {
  const setAddOpen = useCompanyStore((s) => s.setAddOpen);

  return (
    <>
      <DataTable<Company, unknown>
        getColumns={getCompanyColumns}
        fetchDataFn={fetchCompanies}
        idField="id"
        config={{
          enableRowSelection: false,
          enableDateFilter: false,
          enableUrlState: false,
          searchPlaceholder: "Search companies...",
          defaultSortBy: "createdAt",
          defaultSortOrder: "desc",
          columnResizingTableId: "companies-table",
        }}
        exportConfig={{
          entityName: "companies",
          columnMapping: {
            name: "Name",
            slug: "Slug",
            email: "Email",
            phone: "Phone",
            isActive: "Active",
            createdAt: "Created",
          },
          columnWidths: [{ wch: 28 }, { wch: 18 }, { wch: 28 }, { wch: 18 }, { wch: 10 }, { wch: 16 }],
          headers: ["name", "slug", "email", "phone", "isActive", "createdAt"],
        }}
        renderToolbarContent={() => (
          <Button size="sm" onClick={() => setAddOpen(true)}>
            <Plus className="mr-1 h-4 w-4" />
            Add Company
          </Button>
        )}
      />

      <AddCompanyDialog />
      <DeleteCompanyDialog />
    </>
  );
}
