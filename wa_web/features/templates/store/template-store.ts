import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only confirmation-dialog state for Templates. The builder itself is a full page (not a
 * dialog); only Submit-to-Meta and Delete need a confirm step. Server data lives in TanStack Query.
 */
interface TemplateDialogState {
  isSubmitOpen: boolean;
  isDeleteOpen: boolean;
  selectedTemplateId: string | null;
  selectedTemplateName: string | null;

  openSubmit: (id: string, name: string) => void;
  closeSubmit: () => void;
  openDelete: (id: string, name: string) => void;
  closeDelete: () => void;
}

export const useTemplateDialogStore = create<TemplateDialogState>((set) => ({
  isSubmitOpen: false,
  isDeleteOpen: false,
  selectedTemplateId: null,
  selectedTemplateName: null,

  openSubmit: (id, name) => set({ isSubmitOpen: true, selectedTemplateId: id, selectedTemplateName: name }),
  closeSubmit: () => set({ isSubmitOpen: false, selectedTemplateId: null, selectedTemplateName: null }),
  openDelete: (id, name) => set({ isDeleteOpen: true, selectedTemplateId: id, selectedTemplateName: name }),
  closeDelete: () => set({ isDeleteOpen: false, selectedTemplateId: null, selectedTemplateName: null }),
}));

export const useSubmitTemplateDialog = () =>
  useTemplateDialogStore(
    useShallow((s) => ({
      isOpen: s.isSubmitOpen,
      selectedId: s.selectedTemplateId,
      selectedName: s.selectedTemplateName,
      open: s.openSubmit,
      close: s.closeSubmit,
    }))
  );

export const useDeleteTemplateDialog = () =>
  useTemplateDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeleteOpen,
      selectedId: s.selectedTemplateId,
      selectedName: s.selectedTemplateName,
      open: s.openDelete,
      close: s.closeDelete,
    }))
  );
