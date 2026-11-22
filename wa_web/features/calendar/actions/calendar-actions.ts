"use server";

import { createAction } from "@/lib/actions/wrapper";
import { CalendarService } from "@/features/calendar/services/calendar-service";
import {
  appointmentTypeSchema,
  blackoutSchema,
  cancelAppointmentSchema,
  createAppointmentSchema,
  saveBusinessHoursSchema,
  timeZoneSchema,
} from "@/features/calendar/schema/calendar-schema";

// The API gates these on the manage_calendar permission, which CompanyAdmin holds implicitly.
// The wrapper enforces auth + role BEFORE the handler runs (defense-in-depth on top of the API's
// [RequirePermission(manage_calendar)] + JWT company scoping).
const COMPANY_ADMIN = "CompanyAdmin";

// ── Appointment types ────────────────────────────────────────────────────────

export const getAppointmentTypesAction = createAction(
  { name: "getAppointmentTypesAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (enabledOnly?: boolean) => CalendarService.getTypes(enabledOnly ?? false)
);

export const createAppointmentTypeAction = createAction(
  { name: "createAppointmentTypeAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => CalendarService.createType(appointmentTypeSchema.parse(raw))
);

export const updateAppointmentTypeAction = createAction(
  { name: "updateAppointmentTypeAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) => CalendarService.updateType(id, appointmentTypeSchema.parse(raw))
);

export const deleteAppointmentTypeAction = createAction(
  { name: "deleteAppointmentTypeAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CalendarService.deleteType(id)
);

// ── Settings ─────────────────────────────────────────────────────────────────

export const getCalendarSettingsAction = createAction(
  { name: "getCalendarSettingsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async () => CalendarService.getSettings()
);

export const updateTimeZoneAction = createAction(
  { name: "updateTimeZoneAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => CalendarService.updateTimeZone(timeZoneSchema.parse(raw).timeZoneId)
);

export const saveBusinessHoursAction = createAction(
  { name: "saveBusinessHoursAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => CalendarService.saveBusinessHours(saveBusinessHoursSchema.parse(raw).windows)
);

// ── Blackouts ────────────────────────────────────────────────────────────────

export const getBlackoutsAction = createAction(
  { name: "getBlackoutsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (fromUtc: string, toUtc: string) => CalendarService.getBlackouts(fromUtc, toUtc)
);

export const createBlackoutAction = createAction(
  { name: "createBlackoutAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => CalendarService.createBlackout(blackoutSchema.parse(raw))
);

export const deleteBlackoutAction = createAction(
  { name: "deleteBlackoutAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string) => CalendarService.deleteBlackout(id)
);

// ── Availability & appointments ──────────────────────────────────────────────

export const getAvailabilityAction = createAction(
  { name: "getAvailabilityAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (appointmentTypeId: string, from?: string, to?: string) =>
    CalendarService.getAvailability(appointmentTypeId, from, to)
);

export const getAppointmentsAction = createAction(
  { name: "getAppointmentsAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (fromUtc: string, toUtc: string, includeCancelled?: boolean) =>
    CalendarService.getAppointments(fromUtc, toUtc, includeCancelled ?? false)
);

export const createAppointmentAction = createAction(
  { name: "createAppointmentAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (raw: unknown) => CalendarService.createAppointment(createAppointmentSchema.parse(raw))
);

export const rescheduleAppointmentAction = createAction(
  { name: "rescheduleAppointmentAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, startUtc: string) => CalendarService.reschedule(id, startUtc)
);

export const cancelAppointmentAction = createAction(
  { name: "cancelAppointmentAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, raw: unknown) =>
    CalendarService.cancel(id, cancelAppointmentSchema.parse(raw))
);

export const updateAppointmentStatusAction = createAction(
  { name: "updateAppointmentStatusAction", requireAuth: true, requiredRole: COMPANY_ADMIN },
  async (id: string, status: string) => CalendarService.updateStatus(id, status)
);
