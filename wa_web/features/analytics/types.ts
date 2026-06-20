export interface DailyMessageCount {
  date: string;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
}

export interface MessageMetrics {
  totalSent: number;
  totalDelivered: number;
  totalRead: number;
  totalFailed: number;
  daily: DailyMessageCount[];
}

export interface CampaignPerformanceRow {
  id: string;
  name: string;
  status: string;
  launchedAt: string | null;
  totalRecipients: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  skipped: number;
}

export interface CampaignPerformance {
  campaigns: CampaignPerformanceRow[];
}

export interface BillableCategoryCount {
  category: string;
  billable: boolean;
  count: number;
}

export interface CostAnalytics {
  totalBillable: number;
  totalNonBillable: number;
  breakdown: BillableCategoryCount[];
}

export interface AnalyticsDateRange {
  from: string;
  to: string;
}
