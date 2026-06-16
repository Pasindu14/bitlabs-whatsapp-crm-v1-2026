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
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  skipped: number;
  deliveryRate: number;
  readRate: number;
}

export interface CampaignRecipient {
  id: string;
  contactId: string;
  contactName: string;
  contactPhone: string;
  status: string;
  errorCode: string | null;
  messageId: string | null;
  createdAt: string;
}

export interface CampaignListParams {
  page: number;
  pageSize: number;
  search?: string;
  status?: string;
}
