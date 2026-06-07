import { create } from "zustand";
import type { Company } from "@/features/companies/types";

/** UI-only state for the companies screen (dialogs). Server data lives in TanStack Query. */
interface CompanyStore {
  addOpen: boolean;
  setAddOpen: (open: boolean) => void;

  /** Company pending deletion (drives the confirm dialog). */
  deleteTarget: Company | null;
  setDeleteTarget: (company: Company | null) => void;
}

export const useCompanyStore = create<CompanyStore>((set) => ({
  addOpen: false,
  setAddOpen: (addOpen) => set({ addOpen }),

  deleteTarget: null,
  setDeleteTarget: (deleteTarget) => set({ deleteTarget }),
}));
