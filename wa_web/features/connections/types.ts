export type WabaConnectionStatus = "Connected" | "Disconnected" | "Invalid";

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
}
