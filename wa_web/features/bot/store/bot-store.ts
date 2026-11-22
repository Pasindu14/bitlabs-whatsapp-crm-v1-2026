import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Chatbot screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/delete (only one dialog is open at a time).
 */
interface AutoReplyRuleDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isDeleteOpen: boolean;
  selectedRuleId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openDelete: (id: string) => void;
  closeDelete: () => void;
}

export const useAutoReplyRuleDialogStore = create<AutoReplyRuleDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isDeleteOpen: false,
  selectedRuleId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedRuleId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedRuleId: null }),
  openDelete: (id) => set({ isDeleteOpen: true, selectedRuleId: id }),
  closeDelete: () => set({ isDeleteOpen: false, selectedRuleId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateRuleDialog = () =>
  useAutoReplyRuleDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditRuleDialog = () =>
  useAutoReplyRuleDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedRuleId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useDeleteRuleDialog = () =>
  useAutoReplyRuleDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeleteOpen,
      selectedId: s.selectedRuleId,
      open: s.openDelete,
      close: s.closeDelete,
    }))
  );
