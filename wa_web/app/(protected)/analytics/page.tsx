import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { AnalyticsDashboard } from "@/features/analytics/components/analytics-dashboard";

export default async function AnalyticsPage() {
  const session = await auth();
  if (session?.user?.role !== "CompanyAdmin") {
    redirect("/unauthorized");
  }

  return (
    <div className="flex flex-1 flex-col gap-6 p-6">
      <div className="page-header">
        <h1>Analytics</h1>
        <p>
          Message delivery metrics and campaign performance.
        </p>
      </div>

      <AnalyticsDashboard />
    </div>
  );
}
