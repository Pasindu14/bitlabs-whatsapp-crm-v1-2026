export interface CompanyHealth {
  id: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  totalSent30d: number;
  failed30d: number;
  billable30d: number;
  failureRate: number;
  isFlagged: boolean;
  activeCampaigns: number;
  lastMessageAt: string | null;
  qualityRating: string | null;
}

export interface MonitoringData {
  companies: CompanyHealth[];
}
