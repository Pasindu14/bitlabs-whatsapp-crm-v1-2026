import { create } from "zustand";
import { useShallow } from "zustand/react/shallow";

/**
 * UI-only state for the calendar screens. Server data lives in TanStack Query.
 *
 * `anchorDate` is the ISO date (YYYY-MM-DD, in the company's zone) of the week the grid is showing.
 * It lives here rather than in a URL param so navigating between the calendar and its settings and
 * back does not silently reset the operator to "today".
 */
interface CalendarUiState {
  anchorDate: string | null;

  isBookOpen: boolean;
  isCancelOpen: boolean;
  isTypeFormOpen: boolean;
  isBlackoutOpen: boolean;

  /** Shared across the appointment dialogs — only one is open at a time. */
  selectedAppointmentId: string | null;
  /** Null when the type form is creating rather than editing. */
  selectedTypeId: string | null;
  /** Pre-selected slot when booking was started by clicking an empty slot. */
  prefillStartUtc: string | null;

  setAnchorDate: (iso: string) => void;

  openBook: (startUtc?: string) => void;
  closeBook: () => void;
  openCancel: (appointmentId: string) => void;
  closeCancel: () => void;
  openTypeForm: (typeId?: string) => void;
  closeTypeForm: () => void;
  openBlackout: () => void;
  closeBlackout: () => void;
}

export const useCalendarUiStore = create<CalendarUiState>((set) => ({
  anchorDate: null,

  isBookOpen: false,
  isCancelOpen: false,
  isTypeFormOpen: false,
  isBlackoutOpen: false,

  selectedAppointmentId: null,
  selectedTypeId: null,
  prefillStartUtc: null,

  setAnchorDate: (iso) => set({ anchorDate: iso }),

  openBook: (startUtc) => set({ isBookOpen: true, prefillStartUtc: startUtc ?? null }),
  closeBook: () => set({ isBookOpen: false, prefillStartUtc: null }),
  openCancel: (appointmentId) => set({ isCancelOpen: true, selectedAppointmentId: appointmentId }),
  closeCancel: () => set({ isCancelOpen: false, selectedAppointmentId: null }),
  openTypeForm: (typeId) => set({ isTypeFormOpen: true, selectedTypeId: typeId ?? null }),
  closeTypeForm: () => set({ isTypeFormOpen: false, selectedTypeId: null }),
  openBlackout: () => set({ isBlackoutOpen: true }),
  closeBlackout: () => set({ isBlackoutOpen: false }),
}));

// --- Selectors (stable shallow slices) ---

export const useCalendarAnchor = () =>
  useCalendarUiStore(useShallow((s) => ({ anchorDate: s.anchorDate, setAnchorDate: s.setAnchorDate })));

export const useBookDialog = () =>
  useCalendarUiStore(
    useShallow((s) => ({
      isOpen: s.isBookOpen,
      prefillStartUtc: s.prefillStartUtc,
      open: s.openBook,
      close: s.closeBook,
    }))
  );

export const useCancelDialog = () =>
  useCalendarUiStore(
    useShallow((s) => ({
      isOpen: s.isCancelOpen,
      selectedId: s.selectedAppointmentId,
      open: s.openCancel,
      close: s.closeCancel,
    }))
  );

export const useTypeFormDialog = () =>
  useCalendarUiStore(
    useShallow((s) => ({
      isOpen: s.isTypeFormOpen,
      selectedId: s.selectedTypeId,
      open: s.openTypeForm,
      close: s.closeTypeForm,
    }))
  );

export const useBlackoutDialog = () =>
  useCalendarUiStore(
    useShallow((s) => ({ isOpen: s.isBlackoutOpen, open: s.openBlackout, close: s.closeBlackout }))
  );
