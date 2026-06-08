"use client";

import { Cross2Icon } from "@radix-ui/react-icons";
import type { Table } from "@tanstack/react-table";
import { useEffect, useState, useRef, useCallback } from "react";
import { Settings, Undo2, TrashIcon, EyeOff, CheckSquare, MoveHorizontal, Search } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { CalendarDatePicker } from "@/components/calendar-date-picker";
import { DataTableViewOptions } from "./view-options";
import { DataTableExport } from "./data-export";
import type { DataTransformFunction, ExportableData } from "./utils/export-utils";
import { resetUrlState } from "./utils/deep-utils";
import { parseDateFromUrl } from "./utils/url-state";
import type { TableConfig } from "./utils/table-config";
import { formatDate } from "./utils/date-format";

// Helper functions for component sizing
const getInputSizeClass = (size: 'sm' | 'default' | 'lg') => {
  switch (size) {
    case 'sm': return 'h-8';
    case 'lg': return 'h-11';
    default: return '';
  }
};

const getButtonSizeClass = (size: 'sm' | 'default' | 'lg', isIcon = false) => {
  if (isIcon) {
    switch (size) {
      case 'sm': return 'h-8 w-8';
      case 'lg': return 'h-11 w-11';
      default: return '';
    }
  }
  switch (size) {
    case 'sm': return 'h-8 px-3';
    case 'lg': return 'h-11 px-5';
    default: return '';
  }
};

interface DataTableToolbarProps<TData extends ExportableData> {
  table: Table<TData>;
  search: string;
  dateRange: { from_date: string; to_date: string };
  setSearch: (value: string | ((prev: string) => string)) => void;
  setDateRange: (
    value:
      | { from_date: string; to_date: string }
      | ((prev: { from_date: string; to_date: string }) => {
        from_date: string;
        to_date: string;
      })
  ) => void;
  totalSelectedItems?: number;
  deleteSelection?: () => void;
  getSelectedItems?: () => Promise<TData[]>;
  getAllItems?: () => TData[];
  config: TableConfig;
  resetColumnSizing?: () => void;
  resetColumnOrder?: () => void;
  entityName?: string;
  columnMapping?: Record<string, string>;
  columnWidths?: Array<{ wch: number }>;
  headers?: string[];
  transformFunction?: DataTransformFunction<TData>;
  customToolbarComponent?: React.ReactNode;
  // Subrow props
  subRowsConfig?: any;
  getSelectedParentsAndSubrows?: () => { parents: TData[]; subrows: any[]; parentIds: any[]; subrowIds: any[] };
  getSelectedParentRows?: () => Promise<TData[]>;
  getSelectedSubRows?: () => Promise<TData[]>;
  totalParentCount?: number;
  totalSubrowCount?: number;
  enableCsv?: boolean;
  enableExcel?: boolean;
  subRowExportConfig?: {
    entityName: string;
    columnMapping: Record<string, string>;
    columnWidths: Array<{ wch: number }>;
    headers: string[];
    transformFunction?: DataTransformFunction<TData>;
  };
  customFilters?: any;
  setCustomFilters?: (filters: any) => void;
  renderCustomFilters?: (filters: any, setFilters: (filters: any) => void) => React.ReactNode;
}

export function DataTableToolbar<TData extends ExportableData>({
  table,
  search,
  dateRange,
  setSearch,
  setDateRange,
  totalSelectedItems = 0,
  deleteSelection,
  getSelectedItems,
  getAllItems,
  config,
  resetColumnSizing,
  resetColumnOrder,
  entityName = "items",
  columnMapping,
  columnWidths,
  headers,
  transformFunction,
  customToolbarComponent,
  subRowsConfig,
  getSelectedParentsAndSubrows,
  getSelectedParentRows,
  getSelectedSubRows,
  totalParentCount: totalParentCountProp,
  totalSubrowCount: totalSubrowCountProp,
  enableCsv = true,
  enableExcel = true,
  subRowExportConfig,
  customFilters,
  setCustomFilters,
  renderCustomFilters,
}: DataTableToolbarProps<TData>) {
  const tableFiltered = table.getState().columnFilters.length > 0;

  // Calculate parent and subrow selections
  const { parents = [], subrows = [] } = subRowsConfig?.enabled && getSelectedParentsAndSubrows
    ? getSelectedParentsAndSubrows()
    : { parents: [], subrows: [] };

  // Use prop counts if available (cross-page), otherwise use current page counts
  const parentCount = totalParentCountProp !== undefined ? totalParentCountProp : parents.length;
  const subrowCount = totalSubrowCountProp !== undefined ? totalSubrowCountProp : subrows.length;

  // Get search value from table state as fallback
  const currentSearchFromTable =
    (table.getState().globalFilter as string) || "";

  // Local search input state — only committed to parent when user clicks Search or presses Enter.
  const [localSearch, setLocalSearch] = useState(search || currentSearchFromTable);

  // Sync local input when external search state changes (e.g. URL navigation / reset)
  useEffect(() => {
    if (search !== localSearch) {
      setLocalSearch(search);
    }
  }, [search]); // eslint-disable-line react-hooks/exhaustive-deps

  const tableSearch = (table.getState().globalFilter as string) || "";
  // Sync local input when table globalFilter is set externally
  useEffect(() => {
    if (tableSearch !== localSearch && tableSearch !== "") {
      setLocalSearch(tableSearch);
    }
  }, [tableSearch]); // eslint-disable-line react-hooks/exhaustive-deps

  // Reference to track if we're currently updating dates
  const isUpdatingDates = useRef(false);
  
  // Reference to track the last set date values to prevent updates with equal values
  const lastSetDates = useRef<{
    from: Date | undefined;
    to: Date | undefined;
  }>({ from: undefined, to: undefined });

  // Memoize the getInitialDates function to prevent unnecessary recreations
  const getInitialDates = useCallback((): {
    from: Date | undefined;
    to: Date | undefined;
  } => {
    // If we're in the middle of an update, don't re-parse to avoid cycles
    if (isUpdatingDates.current) {
      return lastSetDates.current;
    }

    const fromDate = dateRange?.from_date ? parseDateFromUrl(dateRange.from_date) : undefined;
    const toDate = dateRange?.to_date ? parseDateFromUrl(dateRange.to_date) : undefined;

    lastSetDates.current = { from: fromDate, to: toDate };

    return { from: fromDate, to: toDate };
  }, [dateRange?.from_date, dateRange?.to_date]);

  // Initial state with date values from URL
  const [dates, setDates] = useState<{
    from: Date | undefined;
    to: Date | undefined;
  }>(getInitialDates());

  // Track if user has explicitly changed dates
  const [datesModified, setDatesModified] = useState(
    !!dates.from || !!dates.to
  );

  // Determine if any filters are active
  const isFiltered = tableFiltered || !!localSearch || datesModified;

  // Update the local input value only — search is not sent to the API until submitted
  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setLocalSearch(e.target.value);
  };

  // Commit the current input value to the parent search state (triggers API fetch)
  const handleSearchSubmit = () => {
    setSearch(localSearch.trim());
  };

  // Listen for URL parameter changes and update local state if needed
  useEffect(() => {
    // Skip this effect if we're currently updating the dates ourselves
    if (isUpdatingDates.current) {
      return;
    }
    
    const newDates = getInitialDates();
    
    // Check if dates have actually changed to avoid unnecessary updates
    const hasFromChanged = 
      (newDates.from && !dates.from) || 
      (!newDates.from && dates.from) || 
      (newDates.from && dates.from && newDates.from.getTime() !== dates.from.getTime());
      
    const hasToChanged = 
      (newDates.to && !dates.to) || 
      (!newDates.to && dates.to) || 
      (newDates.to && dates.to && newDates.to.getTime() !== dates.to.getTime());
    
    if (hasFromChanged || hasToChanged) {
      setDates(newDates);
      setDatesModified(!!(newDates.from || newDates.to));
    }
  }, [dates, getInitialDates]);

  // Handle date selection for filtering
  const handleDateSelect = ({ from, to }: { from: Date; to: Date }) => {
    // Compare with previous dates to avoid unnecessary updates
    const hasFromChanged = 
      (from && !dates.from) || 
      (!from && dates.from) || 
      (from && dates.from && from.getTime() !== dates.from.getTime());
      
    const hasToChanged = 
      (to && !dates.to) || 
      (!to && dates.to) || 
      (to && dates.to && to.getTime() !== dates.to.getTime());
    
    // Only update if dates have actually changed
    if (!hasFromChanged && !hasToChanged) {
      return;
    }
    
    // Set flag to prevent update loops
    isUpdatingDates.current = true;
    
    // Update internal state
    setDates({ from, to });
    setDatesModified(true);
    lastSetDates.current = { from, to };

    // Convert dates to strings in YYYY-MM-DD format for the API
    setDateRange({
      from_date: from ? formatDate(from) : "",
      to_date: to ? formatDate(to) : "",
    });
    
    // Reset the updating flag after a delay
    setTimeout(() => {
      isUpdatingDates.current = false;
    }, 100);
  };

  // Reset all filters and URL state
  const handleResetFilters = () => {
    // Reset table filters
    table.resetColumnFilters();

    // Reset search
    setLocalSearch("");
    setSearch("");

    // Reset dates to undefined (no filter)
    setDates({
      from: undefined,
      to: undefined,
    });
    setDatesModified(false);
    setDateRange({
      from_date: "",
      to_date: "",
    });

    // Reset URL state by removing all query parameters, but only if URL state is enabled
    if (config.enableUrlState) {
      resetUrlState();
    }
  };

  // Get selected items data for export - this is now just for the UI indication
  // The actual data fetching happens in the export component
  const selectedItems =
    totalSelectedItems > 0
      ? new Array(totalSelectedItems).fill({} as TData)
      : [];

  // Get all available items data for export
  const allItems = getAllItems ? getAllItems() : [];

  return (
    <div className="flex flex-wrap items-center justify-between">
      <div className="flex flex-1 flex-wrap items-center gap-2">
        {config.enableSearch && (
          <>
            <Input
              placeholder={config.searchPlaceholder || `Search ${entityName}...`}
              value={localSearch}
              onChange={handleSearchChange}
              onKeyDown={(e) => e.key === "Enter" && handleSearchSubmit()}
              className={`w-[150px] lg:w-[250px] ${getInputSizeClass(config.size)}`}
            />
            <Button
              variant="outline"
              size="icon"
              onClick={handleSearchSubmit}
              className={getButtonSizeClass(config.size, true)}
              title="Search"
            >
              <Search className="h-4 w-4" />
              <span className="sr-only">Search</span>
            </Button>
          </>
        )}

        {config.enableDateFilter && (
          <div className="flex items-center">
            <CalendarDatePicker
              date={{
                from: dates.from,
                to: dates.to,
              }}
              onDateSelect={handleDateSelect}
              className={`w-fit cursor-pointer ${getInputSizeClass(config.size)}`}
              variant="outline"
            />
          </div>
        )}

        {renderCustomFilters && setCustomFilters && (
          <div className="flex items-center gap-2">
            {renderCustomFilters(customFilters, setCustomFilters)}
          </div>
        )}

        {isFiltered && (
          <Button
            variant="ghost"
            onClick={handleResetFilters}
            className={getButtonSizeClass(config.size)}
          >
            Reset
            <Cross2Icon className="ml-2 h-4 w-4" />
          </Button>
        )}
      </div>

      <div className="flex items-center gap-2">
        {customToolbarComponent}

        {config.enableExport && (
          <DataTableExport
            table={table}
            data={allItems}
            selectedData={selectedItems}
            getSelectedItems={getSelectedItems}
            entityName={entityName}
            columnMapping={columnMapping}
            columnWidths={columnWidths}
            headers={headers}
            transformFunction={transformFunction}
            size={config.size}
            config={config}
            // Subrow props
            subRowsConfig={subRowsConfig}
            getSelectedParentRows={getSelectedParentRows}
            getSelectedSubRows={getSelectedSubRows}
            parentCount={parentCount}
            subrowCount={subrowCount}
            enableCsv={enableCsv}
            enableExcel={enableExcel}
            subRowExportConfig={subRowExportConfig}
          />
        )}

        {config.enableColumnVisibility && (
          <DataTableViewOptions
            table={table}
            columnMapping={columnMapping}
            size={config.size}
          />
        )}

        <Popover>
          <PopoverTrigger asChild>
            <Button
              variant="outline"
              size="icon"
              className={getButtonSizeClass(config.size, true)}
              title="Table Settings"
            >
              <Settings className="h-4 w-4" />
              <span className="sr-only">Open table settings</span>
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-60" align="end">
            <div className="grid gap-4">
              <div className="space-y-2">
                <h4 className="font-medium leading-none">Table Settings</h4>
              </div>

              <div className="grid gap-2">
                {config.enableColumnResizing && resetColumnSizing && (
                  <Button
                    variant="outline"
                    size={config.size}
                    className="justify-start"
                    onClick={(e) => {
                      e.preventDefault();
                      resetColumnSizing();
                    }}
                  >
                    <Undo2 className="mr-2 h-4 w-4" />
                    Reset Column Sizes
                  </Button>
                )}

                {resetColumnOrder && (
                  <Button
                    variant="outline"
                    size={config.size}
                    className="justify-start"
                    onClick={(e) => {
                      e.preventDefault();
                      resetColumnOrder();
                    }}
                  >
                    <MoveHorizontal className="mr-2 h-4 w-4" />
                    Reset Column Order
                  </Button>
                )}

                {config.enableRowSelection && (
                  <Button
                    variant="outline"
                    size={config.size}
                    className="justify-start"
                    onClick={(e) => {
                      e.preventDefault();
                      table.resetRowSelection();
                      // Also call the parent component's deleteSelection function if available
                      if (deleteSelection) {
                        deleteSelection();
                      }
                    }}
                  >
                    <CheckSquare className="mr-2 h-4 w-4" />
                    Clear Selection
                  </Button>
                )}

                {!table.getIsAllColumnsVisible() && (
                  <Button
                    variant="outline"
                    size={config.size}
                    className="justify-start"
                    onClick={() => table.resetColumnVisibility()}
                  >
                    <EyeOff className="mr-2 h-4 w-4" />
                    Show All Columns
                  </Button>
                )}
              </div>
            </div>
          </PopoverContent>
        </Popover>
      </div>
    </div>
  );
}
