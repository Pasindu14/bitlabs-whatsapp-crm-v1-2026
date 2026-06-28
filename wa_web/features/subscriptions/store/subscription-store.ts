import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only dialog state for the Subscriptions screen. Server data lives in TanStack Query.
 * Change/cancel act on a company (selectedCompanyId); assign opens with no selection.
 */
interface SubscriptionDialogState {
  isAssignOpen: boolean;
  isChangeOpen: boolean;
  isCancelOpen: boolean;
  isAddPackageOpen: boolean;
  selectedCompanyId: string | null;

  openAssign: () => void;
  closeAssign: () => void;
  openChange: (companyId: string) => void;
  closeChange: () => void;
  openCancel: (companyId: string) => void;
  closeCancel: () => void;
  openAddPackage: (companyId: string) => void;
  closeAddPackage: () => void;
}

export const useSubscriptionDialogStore = create<SubscriptionDialogState>((set) => ({
  isAssignOpen: false,
  isChangeOpen: false,
  isCancelOpen: false,
  isAddPackageOpen: false,
  selectedCompanyId: null,

  openAssign: () => set({ isAssignOpen: true }),
  closeAssign: () => set({ isAssignOpen: false }),
  openChange: (companyId) => set({ isChangeOpen: true, selectedCompanyId: companyId }),
  closeChange: () => set({ isChangeOpen: false, selectedCompanyId: null }),
  openCancel: (companyId) => set({ isCancelOpen: true, selectedCompanyId: companyId }),
  closeCancel: () => set({ isCancelOpen: false, selectedCompanyId: null }),
  openAddPackage: (companyId) => set({ isAddPackageOpen: true, selectedCompanyId: companyId }),
  closeAddPackage: () => set({ isAddPackageOpen: false, selectedCompanyId: null }),
}));

// --- Selectors (stable shallow slices) ---

export const useAssignSubscriptionDialog = () =>
  useSubscriptionDialogStore(
    useShallow((s) => ({ isOpen: s.isAssignOpen, open: s.openAssign, close: s.closeAssign }))
  );

export const useChangePlanDialog = () =>
  useSubscriptionDialogStore(
    useShallow((s) => ({
      isOpen: s.isChangeOpen,
      selectedCompanyId: s.selectedCompanyId,
      open: s.openChange,
      close: s.closeChange,
    }))
  );

export const useCancelSubscriptionDialog = () =>
  useSubscriptionDialogStore(
    useShallow((s) => ({
      isOpen: s.isCancelOpen,
      selectedCompanyId: s.selectedCompanyId,
      open: s.openCancel,
      close: s.closeCancel,
    }))
  );

export const useAddPackageDialog = () =>
  useSubscriptionDialogStore(
    useShallow((s) => ({
      isOpen: s.isAddPackageOpen,
      selectedCompanyId: s.selectedCompanyId,
      open: s.openAddPackage,
      close: s.closeAddPackage,
    }))
  );
