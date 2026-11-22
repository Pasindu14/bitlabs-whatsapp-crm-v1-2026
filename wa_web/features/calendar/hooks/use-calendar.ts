"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryKeys } from "@/lib/hooks/query-keys";
import { handleErrorToast } from "@/lib/hooks/use-error-toast";
import type { ActionFailure } from "@/lib/types/actions";
import {
  cancelAppointmentAction,
  createAppointmentAction,
  createAppointmentTypeAction,
  createBlackoutAction,
  deleteAppointmentTypeAction,
  deleteBlackoutAction,
  getAppointmentsAction,
  getAppointmentTypesAction,
  getAvailabilityAction,
  getBlackoutsAction,
  getCalendarSettingsAction,
  rescheduleAppointmentAction,
  saveBusinessHoursAction,
  updateAppointmentStatusAction,
  updateAppointmentTypeAction,
  updateTimeZoneAction,
} from "@/features/calendar/actions/calendar-actions";
import {
  useBlackoutDialog,
  useBookDialog,
  useCancelDialog,
  useTypeFormDialog,
} from "@/features/calendar/store/calendar-store";
import type {
  AppointmentTypeInput,
  BlackoutInput,
  CancelAppointmentInput,
  CreateAppointmentInput,
} from "@/features/calendar/schema/calendar-schema";
import type { BusinessHoursItemInput } from "@/features/calendar/types";

// ── Settings ─────────────────────────────────────────────────────────────────

export function useCalendarSettings() {
  return useQuery({
    queryKey: queryKeys.calendar.settings(),
    queryFn: async () => {
      const res = await getCalendarSettingsAction();
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useUpdateTimeZone() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (timeZoneId: string) => {
      const res = await updateTimeZoneAction({ timeZoneId });
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      // Every slot in the cache was computed against the old zone — drop the lot, not just settings.
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      toast.success("Timezone updated");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Timezone", "update"),
  });
}

export function useSaveBusinessHours() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (windows: BusinessHoursItemInput[]) => {
      const res = await saveBusinessHoursAction({ windows });
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      toast.success("Opening hours saved");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Opening hours", "save"),
  });
}

// ── Appointment types ────────────────────────────────────────────────────────

export function useAppointmentTypes(enabledOnly = false) {
  return useQuery({
    queryKey: queryKeys.calendar.types(enabledOnly),
    queryFn: async () => {
      const res = await getAppointmentTypesAction(enabledOnly);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
  });
}

export function useSaveAppointmentType() {
  const qc = useQueryClient();
  const { close } = useTypeFormDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ id, data }: { id: string | null; data: AppointmentTypeInput }) => {
      const res = id
        ? await updateAppointmentTypeAction(id, data)
        : await createAppointmentTypeAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (_data, vars) => {
      // Changing a service's duration or buffers changes what slots exist, so availability goes too.
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      setFieldErrors(null);
      close();
      toast.success(vars.id ? "Service updated" : "Service created");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Service", "save");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useDeleteAppointmentType() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteAppointmentTypeAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      toast.success("Service retired");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Service", "delete"),
  });
}

// ── Blackouts ────────────────────────────────────────────────────────────────

export function useBlackouts(fromUtc: string, toUtc: string) {
  return useQuery({
    queryKey: queryKeys.calendar.blackouts(fromUtc, toUtc),
    queryFn: async () => {
      const res = await getBlackoutsAction(fromUtc, toUtc);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!fromUtc && !!toUtc,
  });
}

export function useCreateBlackout() {
  const qc = useQueryClient();
  const { close } = useBlackoutDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: BlackoutInput) => {
      const res = await createBlackoutAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      setFieldErrors(null);
      close();
      toast.success("Time blocked");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);
      handleErrorToast(error, "Blackout", "create");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useDeleteBlackout() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const res = await deleteBlackoutAction(id);
      if (!res.success) throw res;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      toast.success("Block removed");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Blackout", "delete"),
  });
}

// ── Availability ─────────────────────────────────────────────────────────────

/** Dates are the company's LOCAL calendar dates (YYYY-MM-DD). */
export function useAvailability(appointmentTypeId: string | null, from?: string, to?: string) {
  return useQuery({
    queryKey: queryKeys.calendar.availability(appointmentTypeId ?? "", from, to),
    queryFn: async () => {
      const res = await getAvailabilityAction(appointmentTypeId!, from, to);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!appointmentTypeId,
    // Slots go stale the moment someone else books one, so never serve them from a warm cache.
    staleTime: 0,
  });
}

// ── Appointments ─────────────────────────────────────────────────────────────

export function useAppointments(fromUtc: string, toUtc: string, includeCancelled = false) {
  return useQuery({
    queryKey: queryKeys.calendar.appointments(fromUtc, toUtc, includeCancelled),
    queryFn: async () => {
      const res = await getAppointmentsAction(fromUtc, toUtc, includeCancelled);
      if (!res.success) throw new Error(res.error);
      return res.data;
    },
    enabled: !!fromUtc && !!toUtc,
  });
}

export function useCreateAppointment() {
  const qc = useQueryClient();
  const { close } = useBookDialog();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | null>(null);

  const mutation = useMutation({
    mutationFn: async (data: CreateAppointmentInput) => {
      const res = await createAppointmentAction(data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      setFieldErrors(null);
      close();
      toast.success("Appointment booked");
    },
    onError: (error: ActionFailure) => {
      if (error.fields) setFieldErrors(error.fields);

      // SLOT_TAKEN is the database's overlap constraint rejecting a booking that was free when the
      // slot list was rendered. Refreshing is the only useful next step, so do it for the operator.
      if (error.code === "SLOT_TAKEN") {
        qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
        toast.error("That time was just booked by someone else. Pick another slot.");
        return;
      }
      handleErrorToast(error, "Appointment", "book");
    },
  });

  return { ...mutation, fieldErrors, clearFieldErrors: () => setFieldErrors(null) };
}

export function useRescheduleAppointment() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, startUtc }: { id: string; startUtc: string }) => {
      const res = await rescheduleAppointmentAction(id, startUtc);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      toast.success("Appointment moved");
    },
    onError: (error: ActionFailure) => {
      if (error.code === "SLOT_TAKEN") {
        qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
        toast.error("That time was just booked by someone else. Pick another slot.");
        return;
      }
      handleErrorToast(error, "Appointment", "reschedule");
    },
  });
}

export function useCancelAppointment() {
  const qc = useQueryClient();
  const { close } = useCancelDialog();

  return useMutation({
    mutationFn: async ({ id, data }: { id: string; data: CancelAppointmentInput }) => {
      const res = await cancelAppointmentAction(id, data);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: (appointment) => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      close();
      if (appointment?.externalSyncStatus === "CancelRequested") {
        // The clinic's API cannot cancel: the booking there is only flagged.
        toast.success("Appointment cancelled", {
          description: `Clinic booking #${appointment.externalBookingId} is flagged for cancellation — cancel it in the clinic CRM too.`,
        });
      } else {
        toast.success("Appointment cancelled");
      }
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Appointment", "cancel"),
  });
}

export function useUpdateAppointmentStatus() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, status }: { id: string; status: string }) => {
      const res = await updateAppointmentStatusAction(id, status);
      if (!res.success) throw res;
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.calendar.all });
      toast.success("Appointment updated");
    },
    onError: (error: ActionFailure) => handleErrorToast(error, "Appointment", "update"),
  });
}
