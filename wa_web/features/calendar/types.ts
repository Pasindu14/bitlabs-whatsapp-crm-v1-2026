/** Lifecycle of a booking (mirrors wa_api AppointmentStatus). */
export type AppointmentStatus =
  | "Pending"
  | "Confirmed"
  | "Cancelled"
  | "Completed"
  | "NoShow";

/** Where a booking came from (mirrors wa_api AppointmentSource). */
export type AppointmentSource = "Bot" | "Agent" | "Admin";

/** Where a booking stands with the clinic's CRM (plan 014). Mirrors wa_api ExternalSyncStatus. */
export type ExternalSyncStatus = "NotApplicable" | "Synced" | "Pending" | "Failed" | "CancelRequested";

/** Weekday, matching .NET's DayOfWeek serialization (Sunday = 0). */
export type DayName =
  | "Sunday"
  | "Monday"
  | "Tuesday"
  | "Wednesday"
  | "Thursday"
  | "Friday"
  | "Saturday";

/** Weekday order used by every schedule grid — Monday first, as a working week reads. */
export const WEEK_DAYS: DayName[] = [
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
  "Sunday",
];

/** A bookable service (mirrors wa_api AppointmentTypeResponse). */
export interface AppointmentType {
  id: string;
  name: string;
  description: string | null;
  /** Length of the appointment as told to the customer. */
  durationMinutes: number;
  /** How often a slot may start inside an opening window. */
  slotIntervalMinutes: number;
  /** Dead time reserved before the appointment — blocks the calendar, invisible to the customer. */
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
  /** Earliest bookable slot, as minutes from now. */
  minimumNoticeMinutes: number;
  bookingHorizonDays: number;
  price: number | null;
  isEnabled: boolean;
  isActive: boolean;
  createdAt: string;
}

/** One opening window in the company's local wall-clock time (mirrors BusinessHoursResponse). */
export interface BusinessHoursWindow {
  id: string;
  dayOfWeek: DayName;
  /** "09:00:00" — local time, deliberately not UTC. */
  startLocal: string;
  endLocal: string;
}

/** Timezone plus the weekly schedule (mirrors CalendarSettingsResponse). */
export interface CalendarSettings {
  /** IANA id, e.g. "Asia/Colombo". */
  timeZoneId: string;
  businessHours: BusinessHoursWindow[];
}

/** A blocked span (mirrors BlackoutResponse). Times are UTC. */
export interface Blackout {
  id: string;
  startUtc: string;
  endUtc: string;
  reason: string;
}

/** One offerable slot (mirrors AvailabilitySlotResponse). */
export interface AvailabilitySlot {
  startUtc: string;
  endUtc: string;
}

/** Result of an availability query (mirrors AvailabilityResponse). */
export interface Availability {
  timeZoneId: string;
  fromDate: string;
  toDate: string;
  slots: AvailabilitySlot[];
}

/** A booking (mirrors AppointmentResponse). All timestamps are UTC. */
export interface Appointment {
  id: string;
  contactId: string;
  contactName: string;
  contactPhone: string;
  appointmentTypeId: string;
  appointmentTypeName: string;
  conversationId: string | null;
  startUtc: string;
  endUtc: string;
  status: AppointmentStatus;
  source: AppointmentSource;
  notes: string | null;
  holdExpiresAt: string | null;
  cancelledAt: string | null;
  cancellationReason: string | null;
  createdAt: string;
  externalSyncStatus: ExternalSyncStatus;
  /** The clinic CRM's booking number; changes every time the booking is moved. */
  externalBookingId: number | null;
  externalSyncError: string | null;
}

/** One window submitted in a weekly schedule save. */
export interface BusinessHoursItemInput {
  dayOfWeek: DayName;
  startLocal: string;
  endLocal: string;
}
