import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Contacts screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface ContactDialogState {
  isCreateOpen: boolean;
  isImportOpen: boolean;
  isEditOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedContactId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openImport: () => void;
  closeImport: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
}

export const useContactDialogStore = create<ContactDialogState>((set) => ({
  isCreateOpen: false,
  isImportOpen: false,
  isEditOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedContactId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openImport: () => set({ isImportOpen: true }),
  closeImport: () => set({ isImportOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedContactId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedContactId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedContactId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedContactId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedContactId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedContactId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateContactDialog = () =>
  useContactDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useImportContactsDialog = () =>
  useContactDialogStore(
    useShallow((s) => ({ isOpen: s.isImportOpen, open: s.openImport, close: s.closeImport }))
  );

export const useEditContactDialog = () =>
  useContactDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedContactId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useActivateContactDialog = () =>
  useContactDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedContactId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivateContactDialog = () =>
  useContactDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedContactId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
