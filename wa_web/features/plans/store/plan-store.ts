import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Plans screen. Server data lives in TanStack Query.
 * One selectedId is shared across edit/activate/deactivate (only one dialog open at a time).
 */
interface PlanDialogState {
  isCreateOpen: boolean;
  isEditOpen: boolean;
  isActivateOpen: boolean;
  isDeactivateOpen: boolean;
  selectedPlanId: string | null;

  openCreate: () => void;
  closeCreate: () => void;
  openEdit: (id: string) => void;
  closeEdit: () => void;
  openActivate: (id: string) => void;
  closeActivate: () => void;
  openDeactivate: (id: string) => void;
  closeDeactivate: () => void;
}

export const usePlanDialogStore = create<PlanDialogState>((set) => ({
  isCreateOpen: false,
  isEditOpen: false,
  isActivateOpen: false,
  isDeactivateOpen: false,
  selectedPlanId: null,

  openCreate: () => set({ isCreateOpen: true }),
  closeCreate: () => set({ isCreateOpen: false }),
  openEdit: (id) => set({ isEditOpen: true, selectedPlanId: id }),
  closeEdit: () => set({ isEditOpen: false, selectedPlanId: null }),
  openActivate: (id) => set({ isActivateOpen: true, selectedPlanId: id }),
  closeActivate: () => set({ isActivateOpen: false, selectedPlanId: null }),
  openDeactivate: (id) => set({ isDeactivateOpen: true, selectedPlanId: id }),
  closeDeactivate: () => set({ isDeactivateOpen: false, selectedPlanId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useCreatePlanDialog = () =>
  usePlanDialogStore(
    useShallow((s) => ({ isOpen: s.isCreateOpen, open: s.openCreate, close: s.closeCreate }))
  );

export const useEditPlanDialog = () =>
  usePlanDialogStore(
    useShallow((s) => ({
      isOpen: s.isEditOpen,
      selectedId: s.selectedPlanId,
      open: s.openEdit,
      close: s.closeEdit,
    }))
  );

export const useActivatePlanDialog = () =>
  usePlanDialogStore(
    useShallow((s) => ({
      isOpen: s.isActivateOpen,
      selectedId: s.selectedPlanId,
      open: s.openActivate,
      close: s.closeActivate,
    }))
  );

export const useDeactivatePlanDialog = () =>
  usePlanDialogStore(
    useShallow((s) => ({
      isOpen: s.isDeactivateOpen,
      selectedId: s.selectedPlanId,
      open: s.openDeactivate,
      close: s.closeDeactivate,
    }))
  );
