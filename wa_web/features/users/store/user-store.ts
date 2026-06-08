import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Users screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface UserDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isResetPasswordOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedUserId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openResetPassword: (id: string) => void;
  closeResetPassword: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
}

export const useUserDialogStore = create<UserDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isResetPasswordOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedUserId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedUserId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedUserId: null }),
  openResetPassword: (id) => set({ isResetPasswordOpen: true, selectedUserId: id }),
  closeResetPassword: () => set({ isResetPasswordOpen: false, selectedUserId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedUserId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedUserId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedUserId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedUserId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateUserDialog = () =>
  useUserDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditUserDialog = () =>
  useUserDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedUserId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useResetPasswordDialog = () =>
  useUserDialogStore(
    useShallow((s) => ({
      isOpen: s.isResetPasswordOpen,
      selectedId: s.selectedUserId,
      open: s.openResetPassword,
      close: s.closeResetPassword,
    }))
  );

export const useActivateUserDialog = () =>
  useUserDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedUserId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivateUserDialog = () =>
  useUserDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedUserId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
