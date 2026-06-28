export type WabaConnectionStatus = "Connected" | "Disconnected" | "Invalid";

/** Meta messaging limit tier (mirrors wa_api MessagingTier enum). */
export type MessagingTier = "Tier250" | "Tier1K" | "Tier10K" | "Tier100K" | "Unlimited";

export interface MyWabaConnection {
  id: string;
  companyId: string;
  companyName: string | null;
  phoneNumberId: string;
  wabaId: string;
  displayPhoneNumber: string;
  status: WabaConnectionStatus;
  hasAccessToken: boolean;
  isActive: boolean;
  createdAt: string;
  lastHealthCheckAt: string | null;
  healthCheckErrorMessage: string | null;
  /** Meta quality rating (GREEN/YELLOW/RED). Null until first synced from Meta. */
  qualityRating: string | null;
  /** Meta messaging limit tier (24-hour unique-recipient cap). */
  messagingTier: MessagingTier;
}
