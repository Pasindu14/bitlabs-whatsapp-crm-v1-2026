export type NotificationType =
  | "QuotaWarning3Days"
  | "QuotaWarning2Days"
  | "QuotaWarning1Day"
  | "QuotaWarningToday"
  | "CampaignFailed"
  | "TemplateApproved"
  | "CampaignThrottled";

export interface Notification {
  id: string;
  campaignId: string | null;
  templateId: string | null;
  type: NotificationType;
  title: string;
  body: string;
  isRead: boolean;
  createdAt: string;
}

export interface NotificationListResponse {
  items: Notification[];
  unreadCount: number;
  totalCount: number;
}

export interface NotificationListParams {
  page: number;
  pageSize: number;
}
