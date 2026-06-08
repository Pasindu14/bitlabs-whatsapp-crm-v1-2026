import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the WABA connections screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface WabaConnectionDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedConnectionId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
}

export const useWabaConnectionDialogStore = create<WabaConnectionDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedConnectionId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedConnectionId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedConnectionId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedConnectionId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedConnectionId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedConnectionId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedConnectionId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateWabaConnectionDialog = () =>
  useWabaConnectionDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditWabaConnectionDialog = () =>
  useWabaConnectionDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedConnectionId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useActivateWabaConnectionDialog = () =>
  useWabaConnectionDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedConnectionId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivateWabaConnectionDialog = () =>
  useWabaConnectionDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedConnectionId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
