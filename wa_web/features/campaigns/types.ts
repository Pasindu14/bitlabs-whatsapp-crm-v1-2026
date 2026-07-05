export type CampaignStatus =
  | "Draft"
  | "Scheduled"
  | "Running"
  | "Paused"
  | "Completed"
  | "Failed"
  | "Cancelled";

export type ScheduleType = "Immediate" | "OneTime" | "Recurring";

/** Full campaign as returned by the API (mirrors CampaignResponse). */
export interface Campaign {
  id: string;
  name: string;
  templateId: string;
  templateName: string;
  contactListIds: string[];
  contactListNames: string[];
  contactIds: string[];
  variableMapping: string;
  status: CampaignStatus;
  scheduleType: ScheduleType;
  scheduledAt: string | null;
  recurrenceCron: string | null;
  totalRecipients: number;
  sentCount: number;
  /** Send to contacts without recorded opt-in (bypasses the consent gate). */
  overrideConsentGate: boolean;
  /** Messages sent to contacts without recorded opt-in (audit counter). */
  noConsentSentCount: number;
  launchedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  updatedAt: string;
  // Index signature required by the @data-table ExportableData constraint.
  [key: string]: string | number | boolean | null | undefined | string[];
}

export interface CampaignStats {
  campaignId: string;
  totalRecipients: number;
  queued: number;
  /** Dispatched to Meta (wamid returned), awaiting the 'sent' status webhook that promotes to Sent. */
  accepted: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  skipped: number;
  /** Messages sent to contacts without recorded opt-in (consent override). Audit metric. */
  sentWithoutConsent: number;
  deliveryRate: number;
  readRate: number;
}

/** Pre-send audience breakdown (mirrors wa_api CampaignAudienceHealthResponse). Buckets are mutually
 *  exclusive and sum to `total`, matching the send-time skip precedence. */
export interface CampaignAudienceHealth {
  total: number;
  sendable: number;
  noConsent: number;
  optedOut: number;
  invalid: number;
  noConsentOverridden: number;
  consentOverride: boolean;
}

export interface CampaignRecipient {
  id: string;
  contactId: string;
  contactName: string;
  contactPhone: string;
  status: string;
  errorCode: string | null;
  /** True when sent despite no recorded opt-in (campaign consent override was on). */
  sentWithoutConsent: boolean;
  messageId: string | null;
  createdAt: string;
}

export interface CampaignListParams {
  page: number;
  pageSize: number;
  search?: string;
  status?: string;
}
