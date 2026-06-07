import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

interface MessageDialogState {
  isSendOpen: boolean;
  selectedContactId: string | null;
  selectedContactName: string | null;
  selectedContactPhone: string | null;

  openSend: (contactId: string, contactName: string, contactPhone: string) => void;
  openNew: () => void;
  closeSend: () => void;
}

export const useMessageDialogStore = create<MessageDialogState>((set) => ({
  isSendOpen: false,
  selectedContactId: null,
  selectedContactName: null,
  selectedContactPhone: null,

  openSend: (contactId, contactName, contactPhone) =>
    set({ isSendOpen: true, selectedContactId: contactId, selectedContactName: contactName, selectedContactPhone: contactPhone }),
  openNew: () =>
    set({ isSendOpen: true, selectedContactId: null, selectedContactName: null, selectedContactPhone: null }),
  closeSend: () =>
    set({ isSendOpen: false, selectedContactId: null, selectedContactName: null, selectedContactPhone: null }),
}));

export const useSendMessageDialog = () =>
  useMessageDialogStore(
    useShallow((s) => ({
      isOpen: s.isSendOpen,
      contactId: s.selectedContactId,
      contactName: s.selectedContactName,
      contactPhone: s.selectedContactPhone,
      open: s.openSend,
      openNew: s.openNew,
      close: s.closeSend,
    }))
  );
