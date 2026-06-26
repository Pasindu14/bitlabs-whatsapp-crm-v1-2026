// Mirrors wa_api Features/AdminReports/Dtos/AdminReportsResponse.cs

export interface PlanPackageRow {
  planId: string;
  planName: string;
  price: number;
  currency: string;
  count: number;
  revenue: number;
}

export interface DailyPackageRow {
  date: string;
  count: number;
  revenue: number;
}

export interface PackagesReport {
  from: string;
  to: string;
  totalPackages: number;
  totalRevenue: number;
  byPlan: PlanPackageRow[];
  byDay: DailyPackageRow[];
}

export interface BalanceRow {
  companyId: string;
  companyName: string;
  planId: string;
  planName: string;
  monthlyQuota: number;
  used: number;
  remaining: number;
  periodEnd: string;
  isLow: boolean;
}

export interface BalancesReport {
  companies: BalanceRow[];
}

export interface DashboardReport {
  totalCompanies: number;
  activeCompanies: number;
  activeSubscriptions: number;
  packagesSoldThisMonth: number;
  revenueThisMonth: number;
  newSignupsThisMonth: number;
  messagesSent30d: number;
  billableMessages30d: number;
  companiesLowBalance: number;
}

export interface CompanyUsageRow {
  companyId: string;
  companyName: string;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  billable: number;
}

export interface UsageReport {
  from: string;
  to: string;
  totalSent: number;
  totalBillable: number;
  companies: CompanyUsageRow[];
}
