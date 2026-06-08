import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Contact Lists screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate/manage (one dialog open at a time).
 */
interface ContactListDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  isManageOpen: boolean;
  isImportOpen: boolean;
  selectedListId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
  openManage: (id: string) => void;
  closeManage: () => void;
  openImport: (id: string) => void;
  closeImport: () => void;
}

export const useContactListDialogStore = create<ContactListDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  isManageOpen: false,
  isImportOpen: false,
  selectedListId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedListId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedListId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedListId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedListId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedListId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedListId: null }),
  openManage: (id) => set({ isManageOpen: true, selectedListId: id }),
  closeManage: () => set({ isManageOpen: false, selectedListId: null }),
  openImport: (id) => set({ isImportOpen: true, selectedListId: id }),
  closeImport: () => set({ isImportOpen: false, selectedListId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateContactListDialog = () =>
  useContactListDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditContactListDialog = () =>
  useContactListDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedListId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useActivateContactListDialog = () =>
  useContactListDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedListId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivateContactListDialog = () =>
  useContactListDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedListId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );

export const useManageContactsDialog = () =>
  useContactListDialogStore(
    useShallow((s) => ({
      isOpen: s.isManageOpen,
      selectedId: s.selectedListId,
      open: s.openManage,
      close: s.closeManage,
    }))
  );

export const useImportContactsDialog = () =>
  useContactListDialogStore(
    useShallow((s) => ({
      isOpen: s.isImportOpen,
      selectedId: s.selectedListId,
      open: s.openImport,
      close: s.closeImport,
    }))
  );
