import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Packages screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface PackageDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedPackageId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
}

export const usePackageDialogStore = create<PackageDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedPackageId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedPackageId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedPackageId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedPackageId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedPackageId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedPackageId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedPackageId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreatePackageDialog = () =>
  usePackageDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditPackageDialog = () =>
  usePackageDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedPackageId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useActivatePackageDialog = () =>
  usePackageDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedPackageId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivatePackageDialog = () =>
  usePackageDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedPackageId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
