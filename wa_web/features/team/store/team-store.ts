import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Team screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface TeamDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isResetPasswordOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedMemberId: string | null;

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

export const useTeamDialogStore = create<TeamDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isResetPasswordOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedMemberId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedMemberId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedMemberId: null }),
  openResetPassword: (id) => set({ isResetPasswordOpen: true, selectedMemberId: id }),
  closeResetPassword: () => set({ isResetPasswordOpen: false, selectedMemberId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedMemberId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedMemberId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedMemberId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedMemberId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreateTeamDialog = () =>
  useTeamDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditTeamDialog = () =>
  useTeamDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedMemberId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useResetTeamPasswordDialog = () =>
  useTeamDialogStore(
    useShallow((s) => ({
      isOpen: s.isResetPasswordOpen,
      selectedId: s.selectedMemberId,
      open: s.openResetPassword,
      close: s.closeResetPassword,
    }))
  );

export const useActivateTeamDialog = () =>
  useTeamDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedMemberId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivateTeamDialog = () =>
  useTeamDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedMemberId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
