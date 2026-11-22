import client, { type ApiSuccessBody, createIdempotencyKey } from "@/lib/api/client";
import { executeService } from "@/lib/services/wrapper";
import type {
  Appointment,
  AppointmentType,
  Availability,
  Blackout,
  BusinessHoursItemInput,
  CalendarSettings,
} from "@/features/calendar/types";
import type {
  AppointmentTypeInput,
  BlackoutInput,
  CancelAppointmentInput,
  CreateAppointmentInput,
} from "@/features/calendar/schema/calendar-schema";

/**
 * Talks to wa_api /api/v1/{appointment-types,calendar,appointments}. The axios client attaches the
 * caller's bearer token; the API enforces the manage_calendar permission and scopes every row to the
 * caller's company.
 */
export const CalendarService = {
  // ── Appointment types ────────────────────────────────────────────────────

  async getTypes(enabledOnly = false): Promise<AppointmentType[]> {
    return executeService(
      { context: "CalendarService", method: "getTypes" },
      async () => {
        const res = await client.get<ApiSuccessBody<AppointmentType[]>>("/api/v1/appointment-types", {
          params: { enabledOnly: enabledOnly || undefined },
        });
        return res.data.data ?? [];
      }
    );
  },

  async createType(input: AppointmentTypeInput): Promise<AppointmentType> {
    return executeService(
      { context: "CalendarService", method: "createType" },
      async () => {
        const res = await client.post<ApiSuccessBody<AppointmentType>>(
          "/api/v1/appointment-types",
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async updateType(id: string, input: AppointmentTypeInput): Promise<AppointmentType> {
    return executeService(
      { context: "CalendarService", method: "updateType" },
      async () => {
        const res = await client.put<ApiSuccessBody<AppointmentType>>(
          `/api/v1/appointment-types/${id}`,
          { ...input, isEnabled: input.isEnabled ?? true }
        );
        return res.data.data;
      }
    );
  },

  async deleteType(id: string): Promise<void> {
    return executeService(
      { context: "CalendarService", method: "deleteType" },
      async () => {
        await client.delete(`/api/v1/appointment-types/${id}`);
      }
    );
  },

  // ── Settings ─────────────────────────────────────────────────────────────

  async getSettings(): Promise<CalendarSettings> {
    return executeService(
      { context: "CalendarService", method: "getSettings" },
      async () => {
        const res = await client.get<ApiSuccessBody<CalendarSettings>>("/api/v1/calendar/settings");
        return res.data.data;
      }
    );
  },

  async updateTimeZone(timeZoneId: string): Promise<CalendarSettings> {
    return executeService(
      { context: "CalendarService", method: "updateTimeZone" },
      async () => {
        const res = await client.put<ApiSuccessBody<CalendarSettings>>(
          "/api/v1/calendar/timezone",
          { timeZoneId }
        );
        return res.data.data;
      }
    );
  },

  /** Replace-all: the whole weekly schedule is submitted at once, which makes this idempotent. */
  async saveBusinessHours(windows: BusinessHoursItemInput[]): Promise<CalendarSettings> {
    return executeService(
      { context: "CalendarService", method: "saveBusinessHours" },
      async () => {
        const res = await client.put<ApiSuccessBody<CalendarSettings>>(
          "/api/v1/calendar/business-hours",
          { windows }
        );
        return res.data.data;
      }
    );
  },

  // ── Blackouts ────────────────────────────────────────────────────────────

  async getBlackouts(fromUtc: string, toUtc: string): Promise<Blackout[]> {
    return executeService(
      { context: "CalendarService", method: "getBlackouts" },
      async () => {
        const res = await client.get<ApiSuccessBody<Blackout[]>>("/api/v1/calendar/blackouts", {
          params: { from: fromUtc, to: toUtc },
        });
        return res.data.data ?? [];
      }
    );
  },

  async createBlackout(input: BlackoutInput): Promise<Blackout> {
    return executeService(
      { context: "CalendarService", method: "createBlackout" },
      async () => {
        const res = await client.post<ApiSuccessBody<Blackout>>(
          "/api/v1/calendar/blackouts",
          input,
          { headers: { "X-Idempotency-Key": createIdempotencyKey() } }
        );
        return res.data.data;
      }
    );
  },

  async deleteBlackout(id: string): Promise<void> {
    return executeService(
      { context: "CalendarService", method: "deleteBlackout" },
      async () => {
        await client.delete(`/api/v1/calendar/blackouts/${id}`);
      }
    );
  },

  // ── Availability ─────────────────────────────────────────────────────────

  /** Dates are the company's LOCAL calendar dates (YYYY-MM-DD); the API clamps them to the horizon. */
  async getAvailability(
    appointmentTypeId: string,
    from?: string,
    to?: string
  ): Promise<Availability> {
    return executeService(
      { context: "CalendarService", method: "getAvailability" },
      async () => {
        const res = await client.get<ApiSuccessBody<Availability>>("/api/v1/calendar/availability", {
          params: { appointmentTypeId, from: from || undefined, to: to || undefined },
        });
        return res.data.data;
      }
    );
  },

  // ── Appointments ─────────────────────────────────────────────────────────

  async getAppointments(
    fromUtc: string,
    toUtc: string,
    includeCancelled = false
  ): Promise<Appointment[]> {
    return executeService(
      { context: "CalendarService", method: "getAppointments" },
      async () => {
        const res = await client.get<ApiSuccessBody<Appointment[]>>("/api/v1/appointments", {
          params: { from: fromUtc, to: toUtc, includeCancelled: includeCancelled || undefined },
        });
        return res.data.data ?? [];
      }
    );
  },

  async createAppointment(input: CreateAppointmentInput): Promise<Appointment> {
    return executeService(
      { context: "CalendarService", method: "createAppointment" },
      async () => {
        const res = await client.post<ApiSuccessBody<Appointment>>("/api/v1/appointments", input, {
          headers: { "X-Idempotency-Key": createIdempotencyKey() },
        });
        return res.data.data;
      }
    );
  },

  async reschedule(id: string, startUtc: string): Promise<Appointment> {
    return executeService(
      { context: "CalendarService", method: "reschedule" },
      async () => {
        const res = await client.put<ApiSuccessBody<Appointment>>(
          `/api/v1/appointments/${id}/reschedule`,
          { startUtc }
        );
        return res.data.data;
      }
    );
  },

  async cancel(id: string, input: CancelAppointmentInput): Promise<Appointment> {
    return executeService(
      { context: "CalendarService", method: "cancel" },
      async () => {
        const res = await client.post<ApiSuccessBody<Appointment>>(
          `/api/v1/appointments/${id}/cancel`,
          input
        );
        return res.data.data;
      }
    );
  },

  async updateStatus(id: string, status: string): Promise<Appointment> {
    return executeService(
      { context: "CalendarService", method: "updateStatus" },
      async () => {
        const res = await client.post<ApiSuccessBody<Appointment>>(
          `/api/v1/appointments/${id}/status`,
          { status }
        );
        return res.data.data;
      }
    );
  },
};
