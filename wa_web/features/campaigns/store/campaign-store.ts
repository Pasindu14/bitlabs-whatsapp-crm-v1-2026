import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

interface CampaignDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isDeleteOpen: boolean;
  isCancelOpen: boolean;
  isRecipientsOpen: boolean;
  isLaunchOpen: boolean;
  selectedCampaignId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openDelete: (id: string) => void;
  closeDelete: () => void;
  openCancel: (id: string) => void;
  closeCancel: () => void;
  openRecipients: (id: string) => void;
  closeRecipients: () => void;
  openLaunch: (id: string) => void;
  closeLaunch: () => void;
}

export const useCampaignDialogStore = create<CampaignDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isDeleteOpen: false,
  isCancelOpen: false,
  isRecipientsOpen: false,
  isLaunchOpen: false,
  selectedCampaignId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedCampaignId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedCampaignId: null }),
  openDelete: (id) => set({ isDeleteOpen: true, selectedCampaignId: id }),
  closeDelete: () => set({ isDeleteOpen: false, selectedCampaignId: null }),
  openCancel: (id) => set({ isCancelOpen: true, selectedCampaignId: id }),
  closeCancel: () => set({ isCancelOpen: false, selectedCampaignId: null }),
  openRecipients: (id) => set({ isRecipientsOpen: true, selectedCampaignId: id }),
  closeRecipients: () => set({ isRecipientsOpen: false, selectedCampaignId: null }),
  openLaunch: (id) => set({ isLaunchOpen: true, selectedCampaignId: id }),
  closeLaunch: () => set({ isLaunchOpen: false, selectedCampaignId: null }),
}));

export const useCreateCampaignDialog = () =>
  useCampaignDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditCampaignDialog = () =>
  useCampaignDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedCampaignId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useDeleteCampaignDialog = () =>
  useCampaignDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeleteOpen,
      selectedId: s.selectedCampaignId,
      open: s.openDelete,
      close: s.closeDelete,
    }))
  );

export const useCancelCampaignDialog = () =>
  useCampaignDialogStore(
    useShallow((s) => ({
      isOpen: s.isCancelOpen,
      selectedId: s.selectedCampaignId,
      open: s.openCancel,
      close: s.closeCancel,
    }))
  );

export const useRecipientsDialog = () =>
  useCampaignDialogStore(
    useShallow((s) => ({
      isOpen: s.isRecipientsOpen,
      selectedId: s.selectedCampaignId,
      open: s.openRecipients,
      close: s.closeRecipients,
    }))
  );

export const useLaunchCampaignDialog = () =>
  useCampaignDialogStore(
    useShallow((s) => ({
      isOpen: s.isLaunchOpen,
      selectedId: s.selectedCampaignId,
      open: s.openLaunch,
      close: s.closeLaunch,
    }))
  );
