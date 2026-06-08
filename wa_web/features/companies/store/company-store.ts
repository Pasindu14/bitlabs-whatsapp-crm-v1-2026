import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the companies screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface CompanyDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedCompanyId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
}

export const useCompanyDialogStore = create<CompanyDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedCompanyId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedCompanyId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedCompanyId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedCompanyId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedCompanyId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedCompanyId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedCompanyId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateCompanyDialog = () =>
  useCompanyDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditCompanyDialog = () =>
  useCompanyDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedCompanyId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useActivateCompanyDialog = () =>
  useCompanyDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedCompanyId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivateCompanyDialog = () =>
  useCompanyDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedCompanyId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
