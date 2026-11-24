/** Mirrors wa_api ClinicCrmServiceMappingResponse. */
export interface ClinicCrmServiceMapping {
  appointmentTypeId: string;
  name: string;
  isEnabled: boolean;
  /** The clinic CRM treatment this service books as; null = not bookable by the bot while connected. */
  externalTreatmentId: number | null;
}

/** Mirrors wa_api ClinicCrmSettingsResponse. The client secret is never sent back — only hasSecret. */
export interface ClinicCrmSettings {
  configured: boolean;
  baseUrl: string | null;
  clientId: string | null;
  hasSecret: boolean;
  isEnabled: boolean;
  /** Tell the CRM (POST /messages) when a customer sends a real message. Only counts while isEnabled. */
  notifyOnMessages: boolean;
  lastSuccessAt: string | null;
  lastError: string | null;
  lastErrorAt: string | null;
  services: ClinicCrmServiceMapping[];
}

/** Mirrors wa_api ClinicCrmTreatmentResponse. */
export interface ClinicCrmTreatment {
  id: number;
  name: string;
  description: string | null;
}

/** Mirrors wa_api ClinicCrmTestResponse. */
export interface ClinicCrmTestResult {
  ok: boolean;
  message: string;
  treatments: ClinicCrmTreatment[];
}
