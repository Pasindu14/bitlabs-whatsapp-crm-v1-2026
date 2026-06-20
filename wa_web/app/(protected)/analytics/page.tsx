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
      <div className="rounded-2xl border bg-muted/40 px-8 py-10">
        <h1 className="text-3xl font-bold tracking-tight">Analytics</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Message delivery metrics, campaign performance, and billable usage breakdown.
        </p>
      </div>

      <AnalyticsDashboard />
    </div>
  );
}
